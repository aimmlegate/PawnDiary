// EVT-02 interaction batch/ambient coverage for Pawn Diary's low-signal interaction batching.
//
// Ordinary vanilla interactions each become their own diary page (see EVT-01). But some groups are
// marked low-stakes in DiaryInteractionGroupDefs.xml with a <batch> policy: repeated rows must NOT
// spam one page per row. Instead they accumulate into a pending batch that flushes to a SINGLE page
// once the policy's threshold (or quiet window) is reached. This suite drives that path through the
// real PlayLog.Add choke point and asserts the two shapes the XML defines:
//   - PairEvent  (the "insults" group): repeated Insult rows between a pawn pair merge into ONE
//     combined pairwise page ("InsultBatch").
//   - AmbientDayNote (the "smalltalk" group): repeated Chitchat rows fold into ONE solo per-pawn
//     day note ("SmallTalkAmbientDay") using the chatter as background texture.
// Plus the negative gate: a disabled group drops the rows entirely (no batch, no page).
//
// Determinism (design/TEST_COVERAGE_PLAN.md §3, EVT-02): real storyteller timing is never used. Each test
// lowers the group's XML <maxEvents> threshold to a tiny value so the count route flushes exactly
// when we add the last row, and the smalltalk <promotion> roll (a per-row RNG that can promote a
// batched moment to its own pairwise page) is disabled so ambient routing is deterministic. Every
// mutated Def field and every in-memory batch entry is restored/cleared in teardown.
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
    /// Proves that low-signal interaction rows accumulate into a pending batch and flush to exactly one
    /// batched diary page on the threshold — not one page per row — for both the PairEvent and
    /// AmbientDayNote batch modes, and that a disabled group drops the rows. Requires a loaded game
    /// because the production capture pipeline ignores interactions at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryInteractionBatchFlowTests
    {
        // Small deterministic flush threshold. Two rows stay pending (no premature page), the third
        // reaches the count and flushes one merged page. Also >= smalltalk's minEventsToWrite (3), so
        // the ambient note is worth writing when it flushes.
        private const int BatchFlushThreshold = 3;

        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static Pawn secondPawn;

        /// <summary>
        /// Opens a scope with the two batching groups this suite drives enabled, creates two isolated
        /// adult colonists, forces both groups' batch thresholds to a tiny deterministic value, disables
        /// the smalltalk promotion roll, and registers cleanup for the in-memory batch stores the shared
        /// harness does not itself restore.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("insults", "smalltalk");
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();

            // Force deterministic count-based flushing on both groups (restored in teardown).
            ForceBatchThreshold(RequireGroup("insults"), BatchFlushThreshold);
            ForceBatchThreshold(RequireGroup("smalltalk"), BatchFlushThreshold);

            // Disable smalltalk's per-row promotion roll so every Chitchat row deterministically routes
            // to the ambient batch instead of a random escape to its own pairwise page.
            DisablePromotionRoll(RequireGroup("smalltalk"));

            // Each test adds its rows in a single RimTest frame with no game tick between them. The
            // dispatcher's generic same-type dedup (a 60-tick safety window against fluke duplicate
            // signals for the same pawn/type/shape) would otherwise collapse the identical same-frame rows
            // into one, so the batch never reaches its flush threshold. Disable that window for the test
            // (restored in teardown); real gameplay spaces interactions across thousands of ticks, so the
            // window never interferes with genuine batch accumulation.
            DiaryTuningDef tuning = DiaryTuning.Current;
            int originalDedupTicks = tuning.genericEventTypeDedupTicks;
            tuning.genericEventTypeDedupTicks = 0;
            scope.RegisterCleanup(() => tuning.genericEventTypeDedupTicks = originalDedupTicks);

            // The shared harness restores events/diaries/log rows but not the private in-memory batch
            // stores; clear any entry that references a test pawn while the pawns are still alive.
            DiaryGameComponent component = scope.Component;
            Pawn a = firstPawn;
            Pawn b = secondPawn;
            scope.RegisterCleanup(() => ClearBatchStateForPawns(component, a, b));
        }

        /// <summary>
        /// Restores every mutation (Def thresholds, promotion flag, in-memory batches, events) and audits
        /// that no test-owned state survived — even if a test threw partway through.
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
        /// EVT-02. Adds repeated Insult rows for one pawn pair: the first rows accumulate with NO diary
        /// page, then the row that reaches the threshold flushes exactly ONE combined pairwise page.
        /// </summary>
        [Test]
        public static void PairBatchAccumulatesThenFlushesOnePairEvent()
        {
            InteractionDef insult = RequireDef<InteractionDef>("Insult");

            // Rows below the threshold accumulate in the pending batch and produce no page yet.
            for (int i = 0; i < BatchFlushThreshold - 1; i++)
            {
                scope.RequireNoNewEvent(() => AddInteractionRow(insult, firstPawn, secondPawn));
            }

            // The threshold-reaching row flushes the batch into one merged pairwise diary event whose
            // defName is the policy's synthetic batch name, not the raw "Insult" def.
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => AddInteractionRow(insult, firstPawn, secondPawn),
                "InsultBatch",
                firstPawn,
                secondPawn);

            scope.RequirePairRefs(diaryEvent, firstPawn, secondPawn);
            RequireContextContains(diaryEvent, "batch=interaction");
            RequireContextContains(diaryEvent, "group=insults");
            RequireContextContains(diaryEvent, "events=" + BatchFlushThreshold);
            PawnDiaryRimTestScope.Require(
                string.Equals(diaryEvent.playLogInteractionDefName, "Insult", StringComparison.Ordinal),
                "The merged batch event should retain the real Insult def for Social-log resolution.");
        }

        /// <summary>
        /// EVT-02. Adds repeated Chitchat rows: the first rows accumulate with NO page, then the row that
        /// reaches the threshold flushes one solo ambient day note for the point-of-view pawn, carrying
        /// the sampled chatter as background evidence rather than one page per line.
        /// </summary>
        [Test]
        public static void AmbientRowsAccumulateThenFlushOneSoloDayNote()
        {
            InteractionDef chitchat = RequireDef<InteractionDef>("Chitchat");

            for (int i = 0; i < BatchFlushThreshold - 1; i++)
            {
                scope.RequireNoNewEvent(() => AddInteractionRow(chitchat, firstPawn, secondPawn));
            }

            // The threshold row flushes the ambient note into one solo page for firstPawn (the recipient
            // pawn gets its own separate note; FireAndRequireEvent asserts exactly one for firstPawn).
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => AddInteractionRow(chitchat, firstPawn, secondPawn),
                "SmallTalkAmbientDay",
                firstPawn,
                null);

            scope.RequireSoloRef(diaryEvent, firstPawn);
            RequireContextContains(diaryEvent, "batch=ambient_day_note");
            RequireContextContains(diaryEvent, "group=smalltalk");
            RequireContextContains(diaryEvent, "events=" + BatchFlushThreshold);
            RequireContextContains(diaryEvent, "participants=");
        }

        /// <summary>
        /// EVT-02. When the batching group is disabled in settings, even a burst of rows past the flush
        /// threshold is dropped at capture: no batch accumulates and no diary page is ever produced.
        /// </summary>
        [Test]
        public static void DisabledGroupDropsBatchedRows()
        {
            InteractionDef insult = RequireDef<InteractionDef>("Insult");

            // Turn the group off (the harness snapshot restores the player's original flags in teardown).
            PawnDiaryMod.Settings.SetGroupEnabled("insults", false);

            // Add more rows than the flush threshold; each is dropped and produces no diary event.
            for (int i = 0; i < BatchFlushThreshold + 1; i++)
            {
                scope.RequireNoNewEvent(() => AddInteractionRow(insult, firstPawn, secondPawn));
            }

            // And nothing was even queued into the in-memory pending stores for the test pawns.
            PawnDiaryRimTestScope.Require(
                !HasPendingBatchStateForPawns(scope.Component, firstPawn, secondPawn),
                "A disabled group must not accumulate any pending interaction batch for the test pawns.");
        }

        // ----- helpers ---------------------------------------------------------------------------

        /// <summary>
        /// Adds one vanilla interaction Social-log row through the real PlayLog.Add choke point the
        /// production Harmony hook observes, tracking it so teardown removes exactly it.
        /// </summary>
        private static void AddInteractionRow(InteractionDef interactionDef, Pawn initiator, Pawn recipient)
        {
            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(
                interactionDef, initiator, recipient);
            if (entry == null)
            {
                throw new AssertionException(
                    "Could not construct the vanilla " + interactionDef.defName + " PlayLog row.");
            }

            scope.TrackPlayLogEntry(entry);
            Find.PlayLog.Add(entry);
        }

        /// <summary>
        /// Lowers a group's XML batch flush threshold to <paramref name="threshold"/> and registers a
        /// cleanup that restores the original value. Fails loudly if the group has no batch policy.
        /// </summary>
        private static void ForceBatchThreshold(DiaryInteractionGroupDef group, int threshold)
        {
            if (group.batch == null)
            {
                throw new AssertionException(
                    "Group '" + group.defName + "' has no <batch> policy for the EVT-02 batch test.");
            }

            int original = group.batch.maxEvents;
            group.batch.maxEvents = threshold;
            scope.RegisterCleanup(() => group.batch.maxEvents = original);
        }

        /// <summary>
        /// Disables a group's promotion roll for the duration of the test, restoring it in cleanup. When
        /// absent this is a no-op. Keeps ambient routing deterministic (no random escape to a pair page).
        /// </summary>
        private static void DisablePromotionRoll(DiaryInteractionGroupDef group)
        {
            if (group.promotion == null)
            {
                return;
            }

            bool original = group.promotion.enabled;
            group.promotion.enabled = false;
            scope.RegisterCleanup(() => group.promotion.enabled = original);
        }

        /// <summary>Removes every in-memory batch entry that references either test pawn.</summary>
        private static void ClearBatchStateForPawns(DiaryGameComponent component, Pawn a, Pawn b)
        {
            if (component == null)
            {
                return;
            }

            HashSet<string> ids = PawnIdSet(a, b);
            RemoveDictionaryKeysReferencing(component, "pendingInteractionBatches", ids);
            RemoveDictionaryKeysReferencing(component, "pendingAmbientInteractionNotes", ids);
            RemoveSetEntriesReferencing(component, "writtenAmbientInteractionNotes", ids);
        }

        /// <summary>True when any pending interaction/ambient batch key references either test pawn.</summary>
        private static bool HasPendingBatchStateForPawns(DiaryGameComponent component, Pawn a, Pawn b)
        {
            if (component == null)
            {
                return false;
            }

            HashSet<string> ids = PawnIdSet(a, b);
            return DictionaryHasKeyReferencing(component, "pendingInteractionBatches", ids)
                || DictionaryHasKeyReferencing(component, "pendingAmbientInteractionNotes", ids);
        }

        private static HashSet<string> PawnIdSet(Pawn a, Pawn b)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            if (a != null)
            {
                ids.Add(a.GetUniqueLoadID());
            }

            if (b != null)
            {
                ids.Add(b.GetUniqueLoadID());
            }

            return ids;
        }

        private static IDictionary ReadDictionaryField(DiaryGameComponent component, string fieldName)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(fieldName, PrivateInstance);
            if (field == null)
            {
                throw new AssertionException(
                    "EVT-02 batch cleanup could not locate private field '" + fieldName + "'.");
            }

            return field.GetValue(component) as IDictionary;
        }

        private static void RemoveDictionaryKeysReferencing(
            DiaryGameComponent component, string fieldName, HashSet<string> ids)
        {
            IDictionary dictionary = ReadDictionaryField(component, fieldName);
            if (dictionary == null || dictionary.Count == 0)
            {
                return;
            }

            List<object> remove = new List<object>();
            foreach (object key in dictionary.Keys)
            {
                if (KeyReferencesAnyId(key as string, ids))
                {
                    remove.Add(key);
                }
            }

            for (int i = 0; i < remove.Count; i++)
            {
                dictionary.Remove(remove[i]);
            }
        }

        private static bool DictionaryHasKeyReferencing(
            DiaryGameComponent component, string fieldName, HashSet<string> ids)
        {
            IDictionary dictionary = ReadDictionaryField(component, fieldName);
            if (dictionary == null || dictionary.Count == 0)
            {
                return false;
            }

            foreach (object key in dictionary.Keys)
            {
                if (KeyReferencesAnyId(key as string, ids))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveSetEntriesReferencing(
            DiaryGameComponent component, string fieldName, HashSet<string> ids)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(fieldName, PrivateInstance);
            if (field == null)
            {
                throw new AssertionException(
                    "EVT-02 batch cleanup could not locate private field '" + fieldName + "'.");
            }

            HashSet<string> set = field.GetValue(component) as HashSet<string>;
            if (set == null || set.Count == 0)
            {
                return;
            }

            set.RemoveWhere(key => KeyReferencesAnyId(key, ids));
        }

        private static bool KeyReferencesAnyId(string key, HashSet<string> ids)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            foreach (string id in ids)
            {
                if (!string.IsNullOrEmpty(id) && key.IndexOf(id, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RequireContextContains(DiaryEvent diaryEvent, string fragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0,
                "The batched event context did not contain '" + fragment + "'.");
        }

        private static DiaryInteractionGroupDef RequireGroup(string defName)
        {
            DiaryInteractionGroupDef group = DefDatabase<DiaryInteractionGroupDef>.GetNamedSilentFail(defName);
            if (group == null)
            {
                throw new AssertionException(
                    "Required interaction group '" + defName + "' was not loaded.");
            }

            return group;
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

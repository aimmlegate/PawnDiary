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
using PawnDiary.Capture;
using PawnDiary.Ingestion;
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
            scope = PawnDiaryRimTestScope.Begin("insults", "smalltalk", "heartfelt");
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();

            // Force deterministic count-based flushing on both groups (restored in teardown).
            ForceBatchThreshold(RequireGroup("insults"), BatchFlushThreshold);
            ForceBatchThreshold(RequireGroup("smalltalk"), BatchFlushThreshold);

            // These fixtures prove batching behavior, not probabilistic volume. Force both eventual
            // aggregate pages to deterministic 1x and restore the player's sparse overrides exactly.
            ForceFrequencyMultiplier(RequireGroup("insults"), 1f);
            ForceFrequencyMultiplier(RequireGroup("smalltalk"), 1f);

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
        /// Replaying one exact source row must be inert before any pair-batch state changes. The two
        /// later unique ids prove the duplicate did not advance Count and force an early flush.
        /// </summary>
        [Test]
        public static void PairBatchRejectsRepeatedPlayLogIdBeforeMutation()
        {
            DiaryInteractionGroupDef group = RequireGroup("insults");
            InteractionDef insult = RequireDef<InteractionDef>("Insult");
            string label = insult.LabelCap.Resolve();

            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, insult, label,
                "first unique row", "first recipient row", 910001));
            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, insult, label,
                "duplicate row must be dropped", "duplicate recipient row must be dropped", 910001));
            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, insult, label,
                "second unique row", "second recipient row", 910002));

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => scope.Component.RecordBatchedInteraction(
                    group, firstPawn, secondPawn, insult, label,
                    "third unique row", "third recipient row", 910003),
                "InsultBatch",
                firstPawn,
                secondPawn);

            scope.RequirePairRefs(diaryEvent, firstPawn, secondPawn);
            RequireContextContains(diaryEvent, "events=" + BatchFlushThreshold);
            PawnDiaryRimTestScope.Require(
                diaryEvent.playLogEntryIds.Count == BatchFlushThreshold
                    && diaryEvent.playLogEntryIds.Contains(910001)
                    && diaryEvent.playLogEntryIds.Contains(910002)
                    && diaryEvent.playLogEntryIds.Contains(910003),
                "The pair batch did not retain exactly the three unique PlayLog ids.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.initiatorText.IndexOf("duplicate row must be dropped", StringComparison.Ordinal) < 0
                    && diaryEvent.recipientText.IndexOf(
                        "duplicate recipient row must be dropped", StringComparison.Ordinal) < 0,
                "A repeated PlayLog id still contributed duplicate pair-batch prose.");
        }

        /// <summary>
        /// Alternating who initiated must still produce one order-independent pair batch whose raw text
        /// and retained B2 mood stay attached to the original event POVs.
        /// </summary>
        [Test]
        public static void PairBatchReversedRowsKeepOriginalPovOwnership()
        {
            ForceMoodSnapshotAlways();
            DiaryInteractionGroupDef group = RequireGroup("insults");
            InteractionDef insult = RequireDef<InteractionDef>("Insult");
            string label = insult.LabelCap.Resolve();

            firstPawn.needs.mood.CurLevel = 0.50f;
            secondPawn.needs.mood.CurLevel = 0.50f;
            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, insult, label,
                "first pawn opened", "second pawn received", 920001));

            // The emotionally extreme row is deliberately reversed: secondPawn is the live initiator,
            // while firstPawn is the live recipient. Production must swap both text and mood candidates.
            firstPawn.needs.mood.CurLevel = 0.05f;
            secondPawn.needs.mood.CurLevel = 0.90f;
            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, secondPawn, firstPawn, insult, label,
                "second pawn initiated reverse", "first pawn received reverse", 920002));

            firstPawn.needs.mood.CurLevel = 0.50f;
            secondPawn.needs.mood.CurLevel = 0.50f;
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => scope.Component.RecordBatchedInteraction(
                    group, firstPawn, secondPawn, insult, label,
                    "first pawn closed", "second pawn closed", 920003),
                "InsultBatch",
                firstPawn,
                secondPawn);

            scope.RequirePairRefs(diaryEvent, firstPawn, secondPawn);
            PawnDiaryRimTestScope.Require(
                diaryEvent.initiatorText.IndexOf("first pawn received reverse", StringComparison.Ordinal) >= 0
                    && diaryEvent.initiatorText.IndexOf(
                        "second pawn initiated reverse", StringComparison.Ordinal) < 0,
                "The reversed row's text escaped the original initiator POV.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.recipientText.IndexOf("second pawn initiated reverse", StringComparison.Ordinal) >= 0
                    && diaryEvent.recipientText.IndexOf(
                        "first pawn received reverse", StringComparison.Ordinal) < 0,
                "The reversed row's text escaped the original recipient POV.");

            string firstMood = diaryEvent.MoodSnapshotForRole(DiaryEvent.InitiatorRole);
            string secondMood = diaryEvent.MoodSnapshotForRole(DiaryEvent.RecipientRole);
            PawnDiaryRimTestScope.Require(
                firstMood.StartsWith("mood=" + DiaryBuckets.MoodBucket(5), StringComparison.Ordinal),
                "The reversed row did not retain firstPawn's recipient-side mood in the initiator slot.");
            PawnDiaryRimTestScope.Require(
                secondMood.StartsWith("mood=" + DiaryBuckets.MoodBucket(90), StringComparison.Ordinal),
                "The reversed row did not retain secondPawn's initiator-side mood in the recipient slot.");
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
        /// Quality Wave B2. Changes the writer's mood across a three-row pair batch and proves the
        /// flushed page keeps the most extreme event-time sample (lower mood wins an equal-distance
        /// tie), not the neutral live mood present when the batch flushes.
        /// </summary>
        [Test]
        public static void PairBatchRetainsMostExtremeEventTimeMood()
        {
            ForceMoodSnapshotAlways();
            InteractionDef insult = RequireDef<InteractionDef>("Insult");

            firstPawn.needs.mood.CurLevel = 0.05f;
            scope.RequireNoNewEvent(() => AddInteractionRow(insult, firstPawn, secondPawn));
            firstPawn.needs.mood.CurLevel = 0.95f;
            scope.RequireNoNewEvent(() => AddInteractionRow(insult, firstPawn, secondPawn));
            firstPawn.needs.mood.CurLevel = 0.50f;

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => AddInteractionRow(insult, firstPawn, secondPawn),
                "InsultBatch",
                firstPawn,
                secondPawn);

            string expectedPrefix = "mood=" + DiaryBuckets.MoodBucket(5);
            string snapshot = diaryEvent.MoodSnapshotForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(
                snapshot.StartsWith(expectedPrefix, StringComparison.Ordinal),
                "The pair batch did not retain the lower tied extreme from event time. Expected '"
                    + expectedPrefix + "', got '" + snapshot + "'.");
            PawnDiaryRimTestScope.Require(
                snapshot.IndexOf(DiaryBuckets.MoodBucket(50), StringComparison.Ordinal) < 0,
                "The pair batch re-read the neutral flush-time mood instead of frozen evidence.");
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrEmpty(
                    diaryEvent.MoodSnapshotForRole(DiaryEvent.RecipientRole)),
                "The pair batch did not evaluate and freeze the recipient POV independently.");
        }

        /// <summary>
        /// Quality Wave B2. Proves an ambient day-note retains the writer's most extreme incoming
        /// mood candidate and carries it through the solo frozen-mood factory at count-based flush.
        /// </summary>
        [Test]
        public static void AmbientBatchRetainsMostExtremeEventTimeMood()
        {
            ForceMoodSnapshotAlways();
            InteractionDef chitchat = RequireDef<InteractionDef>("Chitchat");

            firstPawn.needs.mood.CurLevel = 0.05f;
            scope.RequireNoNewEvent(() => AddInteractionRow(chitchat, firstPawn, secondPawn));
            firstPawn.needs.mood.CurLevel = 0.95f;
            scope.RequireNoNewEvent(() => AddInteractionRow(chitchat, firstPawn, secondPawn));
            firstPawn.needs.mood.CurLevel = 0.50f;

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => AddInteractionRow(chitchat, firstPawn, secondPawn),
                "SmallTalkAmbientDay",
                firstPawn,
                null);

            string expectedPrefix = "mood=" + DiaryBuckets.MoodBucket(5);
            string snapshot = diaryEvent.MoodSnapshotForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(
                snapshot.StartsWith(expectedPrefix, StringComparison.Ordinal),
                "The ambient batch did not retain the lower tied extreme from event time. Expected '"
                    + expectedPrefix + "', got '" + snapshot + "'.");
            PawnDiaryRimTestScope.Require(
                snapshot.IndexOf(DiaryBuckets.MoodBucket(50), StringComparison.Ordinal) < 0,
                "The ambient batch re-read the neutral flush-time mood instead of frozen evidence.");
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

        /// <summary>
        /// Direct and promotion-winning interactions carry their exact group into shared admission,
        /// while delayed pair/ambient contributors defer that decision to the aggregate they open.
        /// </summary>
        [Test]
        public static void InteractionSignalCarriesDirectAndDeferredFrequencyOwnership()
        {
            InteractionDef deepTalk = RequireDef<InteractionDef>("DeepTalk");
            CaptureContext direct = new InteractionSignal(
                firstPawn, secondPawn, deepTalk, "direct", "direct", -1).BuildContext();
            RequireFrequencyContext(direct, "heartfelt", false);

            DiaryInteractionGroupDef smalltalk = RequireGroup("smalltalk");
            InteractionDef chitchat = RequireDef<InteractionDef>("Chitchat");
            CaptureContext deferred = new InteractionSignal(
                firstPawn, secondPawn, chitchat, "deferred", "deferred", -1).BuildContext();
            RequireFrequencyContext(deferred, "smalltalk", true);

            ForcePromotionAlways(smalltalk);
            CaptureContext promoted = new InteractionSignal(
                firstPawn, secondPawn, chitchat, "promoted", "promoted", -1).BuildContext();
            RequireFrequencyContext(promoted, "smalltalk", false);
        }

        /// <summary>
        /// A pair batch freezes its admission when the first row opens it. Raising frequency before
        /// the threshold cannot reroll the same candidate; rejection clears the aggregate with no page.
        /// </summary>
        [Test]
        public static void PairBatchFreezesRejectedFrequencyUntilSettlement()
        {
            DiaryInteractionGroupDef group = RequireGroup("insults");
            InteractionDef insult = RequireDef<InteractionDef>("Insult");
            string label = insult.LabelCap.Resolve();
            ForceFrequencyMultiplier(group, 0f);

            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, insult, label, "first", "first", 930001));
            RequirePendingFrequency(
                scope.Component, "pendingInteractionBatches", firstPawn, secondPawn, "insults", false);

            // The open batch owns the old rejection. This affects only later candidates, not this one.
            PawnDiaryMod.Settings.SetGroupFrequencyOverride(group.defName, 1f);
            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, insult, label, "second", "second", 930002));
            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, insult, label, "third", "third", 930003));

            PawnDiaryRimTestScope.Require(
                !HasPendingBatchStateForPawns(scope.Component, firstPawn, secondPawn),
                "A frequency-rejected pair aggregate did not settle and clear at its normal threshold.");
        }

        /// <summary>
        /// Each pawn/day ambient note freezes its own admission. A rejected note settles at the normal
        /// threshold and writes the day guard, so later chatter cannot reopen and reroll that page.
        /// </summary>
        [Test]
        public static void AmbientNoteFreezesRejectedFrequencyAndMarksDayWritten()
        {
            DiaryInteractionGroupDef group = RequireGroup("smalltalk");
            InteractionDef chitchat = RequireDef<InteractionDef>("Chitchat");
            string label = chitchat.LabelCap.Resolve();
            ForceFrequencyMultiplier(group, 0f);

            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, chitchat, label, "first", "first", 940001));
            RequirePendingFrequency(
                scope.Component, "pendingAmbientInteractionNotes", firstPawn, secondPawn, "smalltalk", false);

            PawnDiaryMod.Settings.SetGroupFrequencyOverride(group.defName, 1f);
            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, chitchat, label, "second", "second", 940002));
            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, chitchat, label, "third", "third", 940003));

            PawnDiaryRimTestScope.Require(
                !HasPendingBatchStateForPawns(scope.Component, firstPawn, secondPawn),
                "A frequency-rejected ambient aggregate did not settle at its normal threshold.");
            RequireSetContainsPawnKey(
                scope.Component, "writtenAmbientInteractionNotes", firstPawn);
            RequireSetContainsPawnKey(
                scope.Component, "writtenAmbientInteractionNotes", secondPawn);
        }

        /// <summary>
        /// A pre-save flush below minEventsToWrite must retain an accepted pawn/group/day admission.
        /// Reopening after the setting becomes 0x therefore still writes at the normal threshold instead
        /// of drawing again; the carry-forward key is admission state only, never a written-page guard.
        /// </summary>
        [Test]
        public static void AcceptedThinAmbientPreSaveFlushReopensWithoutReroll()
        {
            DiaryInteractionGroupDef group = RequireGroup("smalltalk");
            InteractionDef chitchat = RequireDef<InteractionDef>("Chitchat");
            string label = chitchat.LabelCap.Resolve();
            int originalMinimum = group.batch.minEventsToWrite;
            group.batch.minEventsToWrite = BatchFlushThreshold;
            scope.RegisterCleanup(() => group.batch.minEventsToWrite = originalMinimum);

            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, chitchat, label, "before save", "before save", 950001));
            InvokePrivateNoArgs(scope.Component, "FlushAllInteractionBatches");

            RequireListContainsPawnKey(
                scope.Component, "acceptedAmbientInteractionFrequencyKeys", firstPawn, true);
            RequireListContainsPawnKey(
                scope.Component, "acceptedAmbientInteractionFrequencyKeys", secondPawn, true);
            RequireSetContainsPawnKey(
                scope.Component, "writtenAmbientInteractionNotes", firstPawn, false);
            RequireSetContainsPawnKey(
                scope.Component, "writtenAmbientInteractionNotes", secondPawn, false);

            // If reopening rerolls, this deterministic 0x override rejects both notes. Reusing the saved
            // acceptance is the only route by which the threshold-reaching row can still create pages.
            PawnDiaryMod.Settings.SetGroupFrequencyOverride(group.defName, 0f);
            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, chitchat, label, "after save one", "after save one", 950002));
            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, chitchat, label, "after save two", "after save two", 950003));
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => scope.Component.RecordBatchedInteraction(
                    group, firstPawn, secondPawn, chitchat, label,
                    "after save three", "after save three", 950004),
                "SmallTalkAmbientDay",
                firstPawn,
                null);

            scope.RequireSoloRef(diaryEvent, firstPawn);
            RequireListContainsPawnKey(
                scope.Component, "acceptedAmbientInteractionFrequencyKeys", firstPawn, false);
            RequireListContainsPawnKey(
                scope.Component, "acceptedAmbientInteractionFrequencyKeys", secondPawn, false);
        }

        /// <summary>
        /// A first threshold-reaching flush can fail before repository registration (for example, a
        /// broken third-party context getter during pre-save). The removed pending note must still leave
        /// its accepted admission behind, so reopening under 0x retries the page without a new roll.
        /// </summary>
        [Test]
        public static void AcceptedAmbientFactoryFaultBeforeCommitRetainsAdmission()
        {
            DiaryInteractionGroupDef group = RequireGroup("smalltalk");
            InteractionDef chitchat = RequireDef<InteractionDef>("Chitchat");
            string label = chitchat.LabelCap.Resolve();
            int originalMinimum = group.batch.minEventsToWrite;
            int originalMaximum = group.batch.maxEvents;
            group.batch.minEventsToWrite = 2;
            group.batch.maxEvents = 100;
            scope.RegisterCleanup(() =>
            {
                group.batch.minEventsToWrite = originalMinimum;
                group.batch.maxEvents = originalMaximum;
            });

            scope.RequireNoNewEvent(() => scope.Component.RecordBatchedInteraction(
                group, firstPawn, secondPawn, chitchat, label,
                "fault candidate", "fault candidate", 950101));

            IDictionary pending = ReadDictionaryField(
                scope.Component, "pendingAmbientInteractionNotes");
            string key = null;
            object note = null;
            string firstPawnId = firstPawn.GetUniqueLoadID();
            foreach (DictionaryEntry entry in pending)
            {
                string candidateKey = entry.Key as string;
                if (!string.IsNullOrEmpty(candidateKey)
                    && candidateKey.IndexOf(firstPawnId, StringComparison.Ordinal) >= 0)
                {
                    key = candidateKey;
                    note = entry.Value;
                    break;
                }
            }

            Type noteType = note?.GetType();
            FieldInfo countField = noteType?.GetField(
                "eventCount", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo samplesField = noteType?.GetField(
                "sampleLines", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo flush = typeof(DiaryGameComponent).GetMethod(
                "FlushAmbientInteractionNote", PrivateInstance);
            PawnDiaryRimTestScope.Require(
                key != null && note != null && countField != null && samplesField != null && flush != null,
                "The ambient pre-commit fault fixture could not resolve its pending note adapter.");

            countField.SetValue(note, 2);
            samplesField.SetValue(note, null);
            bool threw = false;
            try
            {
                flush.Invoke(scope.Component, new[] { (object)key, note });
            }
            catch (TargetInvocationException exception)
            {
                threw = exception.InnerException != null;
            }
            PawnDiaryRimTestScope.Require(
                threw,
                "The ambient fault fixture did not fail before page registration as intended.");

            RequireListContainsPawnKey(
                scope.Component, "acceptedAmbientInteractionFrequencyKeys", firstPawn, true);
            group.batch.minEventsToWrite = 1;
            group.batch.maxEvents = 1;
            PawnDiaryMod.Settings.SetGroupFrequencyOverride(group.defName, 0f);
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => scope.Component.RecordBatchedInteraction(
                    group, firstPawn, secondPawn, chitchat, label,
                    "retry", "retry", 950102),
                "SmallTalkAmbientDay",
                firstPawn,
                null);

            scope.RequireSoloRef(diaryEvent, firstPawn);
            RequireListContainsPawnKey(
                scope.Component, "acceptedAmbientInteractionFrequencyKeys", firstPawn, false);
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

            bool originalEnabled = group.batch.enabled;
            int original = group.batch.maxEvents;
            group.batch.enabled = true;
            group.batch.maxEvents = threshold;
            scope.RegisterCleanup(() =>
            {
                group.batch.enabled = originalEnabled;
                group.batch.maxEvents = original;
            });
        }

        /// <summary>Forces one exact frequency multiplier and restores its prior sparse state.</summary>
        private static void ForceFrequencyMultiplier(
            DiaryInteractionGroupDef group,
            float multiplier)
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            float previous;
            bool hadOverride = settings.TryGetGroupFrequencyOverride(group.defName, out previous);
            float inherited = settings.PresetGroupFrequencyMultiplier(group);
            settings.SetGroupFrequencyOverride(group.defName, multiplier);
            scope.RegisterCleanup(() => settings.SetGroupFrequencyOverride(
                group.defName,
                hadOverride ? previous : inherited));
        }

        /// <summary>Forces the native promotion route to win and restores every touched policy field.</summary>
        private static void ForcePromotionAlways(DiaryInteractionGroupDef group)
        {
            InteractionPromotionPolicy promotion = group?.promotion;
            if (promotion == null)
            {
                throw new AssertionException(
                    "Group '" + group?.defName + "' has no promotion policy for this fixture.");
            }

            bool originalEnabled = promotion.enabled;
            float originalBaseChance = promotion.baseChance;
            float originalMaximumChance = promotion.maxChance;
            promotion.enabled = true;
            promotion.baseChance = 1f;
            promotion.maxChance = 1f;
            scope.RegisterCleanup(() =>
            {
                promotion.enabled = originalEnabled;
                promotion.baseChance = originalBaseChance;
                promotion.maxChance = originalMaximumChance;
            });
        }

        private static void RequireFrequencyContext(
            CaptureContext context,
            string groupKey,
            bool bypass)
        {
            PawnDiaryRimTestScope.Require(
                context != null
                    && string.Equals(context.FrequencyGroupKey, groupKey, StringComparison.Ordinal)
                    && Math.Abs(context.NativeCaptureChance - 1f) < 0.0001f
                    && context.BypassFrequency == bypass,
                "Interaction frequency ownership was not frozen as group='" + groupKey
                    + "', native=1, bypass=" + bypass + ".");
        }

        /// <summary>Asserts all test-pawn pending rows carry one exact frozen frequency result.</summary>
        private static void RequirePendingFrequency(
            DiaryGameComponent component,
            string fieldName,
            Pawn a,
            Pawn b,
            string groupKey,
            bool accepted)
        {
            IDictionary dictionary = ReadDictionaryField(component, fieldName);
            HashSet<string> ids = PawnIdSet(a, b);
            int matching = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!KeyReferencesAnyId(entry.Key as string, ids))
                {
                    continue;
                }

                object pending = entry.Value;
                Type pendingType = pending?.GetType();
                FieldInfo keyField = pendingType?.GetField(
                    "frequencyGroupKey", BindingFlags.Instance | BindingFlags.Public);
                FieldInfo acceptedField = pendingType?.GetField(
                    "frequencyAdmissionAccepted", BindingFlags.Instance | BindingFlags.Public);
                PawnDiaryRimTestScope.Require(
                    keyField != null
                        && acceptedField != null
                        && string.Equals(keyField.GetValue(pending) as string, groupKey,
                            StringComparison.Ordinal)
                        && (bool)acceptedField.GetValue(pending) == accepted,
                    "Pending interaction aggregate did not retain its exact frozen group/result.");
                matching++;
            }

            PawnDiaryRimTestScope.Require(
                matching > 0,
                "No test-owned pending aggregate was available for frequency inspection.");
        }

        private static void RequireSetContainsPawnKey(
            DiaryGameComponent component,
            string fieldName,
            Pawn pawn,
            bool expected = true)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(fieldName, PrivateInstance);
            HashSet<string> values = field?.GetValue(component) as HashSet<string>;
            string pawnId = pawn?.GetUniqueLoadID();
            bool found = false;
            if (values != null && !string.IsNullOrEmpty(pawnId))
            {
                foreach (string value in values)
                {
                    if (!string.IsNullOrEmpty(value)
                        && value.IndexOf(pawnId, StringComparison.Ordinal) >= 0)
                    {
                        found = true;
                        break;
                    }
                }
            }
            PawnDiaryRimTestScope.Require(
                found == expected,
                "Expected private set '" + fieldName + "' "
                    + (expected ? "to contain" : "not to contain")
                    + " a key for '" + pawnId + "'.");
        }

        private static void RequireListContainsPawnKey(
            DiaryGameComponent component,
            string fieldName,
            Pawn pawn,
            bool expected)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(fieldName, PrivateInstance);
            List<string> values = field?.GetValue(component) as List<string>;
            string pawnId = pawn?.GetUniqueLoadID();
            bool found = values != null && values.Exists(value =>
                !string.IsNullOrEmpty(value)
                    && !string.IsNullOrEmpty(pawnId)
                    && value.IndexOf(pawnId, StringComparison.Ordinal) >= 0);
            PawnDiaryRimTestScope.Require(
                found == expected,
                "Expected private list '" + fieldName + "' "
                    + (expected ? "to contain" : "not to contain")
                    + " a key for '" + pawnId + "'.");
        }

        private static void InvokePrivateNoArgs(DiaryGameComponent component, string methodName)
        {
            MethodInfo method = typeof(DiaryGameComponent).GetMethod(methodName, PrivateInstance);
            if (method == null)
            {
                throw new AssertionException(
                    "EVT-02 fixture could not locate private method '" + methodName + "'.");
            }
            method.Invoke(component, null);
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

        /// <summary>
        /// Makes B2 inclusion deterministic for a loaded fixture and restores every authored tuning
        /// float afterward. The production deterministic hash still runs; a probability of one simply
        /// guarantees that any valid [0,1) roll is accepted.
        /// </summary>
        private static void ForceMoodSnapshotAlways()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            float originalApplyChance = tuning.moodSnapshotApplyChance;
            tuning.moodSnapshotApplyChance = 1f;

            List<MoodSnapshotChanceRule> rules = tuning.moodSnapshotChances;
            float[] originalRuleChances = rules == null ? null : new float[rules.Count];
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    MoodSnapshotChanceRule rule = rules[i];
                    if (rule == null)
                    {
                        continue;
                    }

                    originalRuleChances[i] = rule.chance;
                    rule.chance = 1f;
                }
            }

            scope.RegisterCleanup(() =>
            {
                tuning.moodSnapshotApplyChance = originalApplyChance;
                if (rules == null || originalRuleChances == null)
                {
                    return;
                }

                int count = Math.Min(rules.Count, originalRuleChances.Length);
                for (int i = 0; i < count; i++)
                {
                    if (rules[i] != null)
                    {
                        rules[i].chance = originalRuleChances[i];
                    }
                }
            });
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
            RemoveListEntriesReferencing(component, "rejectedAmbientInteractionFrequencyKeys", ids);
            RemoveListEntriesReferencing(component, "acceptedAmbientInteractionFrequencyKeys", ids);
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

        private static void RemoveListEntriesReferencing(
            DiaryGameComponent component, string fieldName, HashSet<string> ids)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(fieldName, PrivateInstance);
            if (field == null)
            {
                throw new AssertionException(
                    "EVT-02 batch cleanup could not locate private field '" + fieldName + "'.");
            }

            List<string> values = field.GetValue(component) as List<string>;
            values?.RemoveAll(key => KeyReferencesAnyId(key, ids));
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

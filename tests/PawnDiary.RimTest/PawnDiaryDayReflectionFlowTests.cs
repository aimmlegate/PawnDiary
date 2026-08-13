// In-game flow tests for Pawn Diary's end-of-day reflection (EVT-19 Day/quadrum reflection).
//
// The real trigger is the sleep-path scan FlushAmbientNotesForSleepingPawns, which iterates every
// resting colonist on every map and calls the unified per-pawn reflection arbitration.
// Reproducing the storyteller, a loaded map, and a bedded-down colonist is neither deterministic nor
// mapless, so — following the Phase 1 pattern (drive the per-unit production work directly) — these
// tests seed the pawn/day evidence store the live AddHediff hook fills (pendingDayHediffs) and then
// invoke the retained dev/test seam by reflection. It enters the same arbitration, gathers day candidates,
// runs the weighted highlight selection, consumes the pending evidence, and dispatches one
// DayReflection page through the shared bus — exactly the work the sleep scan does per colonist.
//
// Determinism: tests that dispatch a day page seed exactly ONE important candidate, forcing the
// weighted selection. The quadrum-gate regression inspects the production opportunity before
// dispatch, so its five candidates never enter random highlight selection. The XML-backed reflection
// tuning that gates the flush (day-summary master switch, the important-signal-kind list, and the
// rarer arc/quadrum reflections that would otherwise pre-empt the daily one) is snapshotted and
// forced to known values, then restored in teardown.
//
// Coverage-matrix ID (design/TEST_COVERAGE_PLAN.md §3): EVT-19.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the day-reflection flush turns seeded day evidence into exactly one solo reflection page,
    /// that the once-per-day guard blocks a second flush, and that the flush consumes the pending
    /// day-evidence store. Requires a loaded game because the reflection pipeline (like every capture
    /// path) is inert at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryDayReflectionFlowTests
    {
        private const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // A distinctive synthetic hediff defName so the seeded evidence's signal tag ("hediff:<defName>")
        // is unambiguous in the emitted gameContext and cannot collide with a real affliction.
        private const string SeedHediffDefName = "PawnDiaryDayReflectionTestHediff";

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        // Snapshot of the reflection tuning fields this suite forces, restored in teardown.
        private static DiaryTuningDef tuning;
        private static bool savedDaySummaryEnabled;
        private static bool savedQuadrumEnabled;
        private static bool savedQuadrumCallbackEnabled;
        private static bool savedArcEnabled;
        private static List<string> savedImportantKinds;
        private static int savedQuadrumMinImportantEntries;

        /// <summary>
        /// Opens a fresh scope with the Reflection group enabled (the day/quadrum reflection user
        /// toggle), forces the reflection tuning to a deterministic state, and creates one isolated
        /// adult colonist with generation disabled.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            // DayReflectionSignal.BuildContext gates userEnabled on the Reflection-domain row that
            // claims the page. Day and quadrum own separate rows since they moved out of the
            // Interaction domain (where they had been unreachable); "reflection" stays on for the
            // generic row those two used to share.
            scope = PawnDiaryRimTestScope.Begin("reflection", "dayreflection", "quadrumreflection");
            pawn = scope.CreateAdultColonist();

            ForceDeterministicReflectionTuning();
            MarkOrdinaryFixtureAsAlreadyBaselined();
            RegisterReflectionGuardCleanup();
        }

        /// <summary>
        /// Restores every mutation (tuning fields, the transient written-reflection guards) and audits
        /// that no test-owned state survived, even when a test threw partway through.
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
                tuning = null;
                savedImportantKinds = null;
            }
        }

        /// <summary>
        /// EVT-19. Seeds one important day-evidence signal, invokes the per-pawn flush, and verifies it
        /// emits exactly one solo DayReflection page whose context reflects the seeded highlight
        /// (day_reflection marker, one highlight, and the seeded hediff signal tag).
        /// </summary>
        [Test]
        public static void SeededDayFlushesOneSoloReflectionWithHighlightContext()
        {
            int day = CurrentDayIndex();
            // This fixture asserts an exact highlight count, and the H3 collector reads the developer's
            // real letter archive — a letter their colony filed today is a legitimate second candidate.
            // Colony news is not what EVT-19 is about, so switch that one collector off outright rather
            // than hoping the live archive is empty.
            SuppressColonyNewsForThisTest();
            SeedPendingDayHediff(day);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeFlush(pawn),
                "DayReflection",
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);

            string context = diaryEvent.gameContext ?? string.Empty;
            PawnDiaryRimTestScope.Require(
                context.IndexOf("day_reflection=true", StringComparison.OrdinalIgnoreCase) >= 0,
                "The reflection context did not carry the day_reflection marker.");
            PawnDiaryRimTestScope.Require(
                context.IndexOf("highlights=1", StringComparison.Ordinal) >= 0,
                "The reflection context did not report the single selected highlight.");
            PawnDiaryRimTestScope.Require(
                context.IndexOf("hediff:" + SeedHediffDefName, StringComparison.Ordinal) >= 0,
                "The reflection context did not include the seeded hediff highlight tag.");
        }

        /// <summary>
        /// EVT-19. The once-per-day guard: after a pawn writes a reflection, a second flush the same day
        /// produces no new page even when fresh evidence is waiting — the guard short-circuits before it
        /// would consume that evidence.
        /// </summary>
        [Test]
        public static void SecondFlushSameDayIsBlockedByOncePerDayGuard()
        {
            int day = CurrentDayIndex();
            SeedPendingDayHediff(day);

            // First flush emits and marks the pawn/day written.
            scope.FireAndRequireEvent(
                () => InvokeFlush(pawn),
                "DayReflection",
                pawn,
                null);

            // Re-seed fresh evidence, then flush again: the guard must drop it with no new page.
            SeedPendingDayHediff(day);
            scope.RequireNoNewEvent(() => InvokeFlush(pawn));

            // Because the guard returns before evidence consumption, the re-seeded batch is untouched.
            PawnDiaryRimTestScope.Require(
                PendingDayHediffContains(DaySummaryKey(pawn, day)),
                "The blocked second flush should have left the re-seeded day evidence in place.");
        }

        /// <summary>
        /// EVT-19. A successful flush consumes the pending day-evidence batch it summarized, so the same
        /// evidence can never separately re-emit on a later day rollover or save.
        /// </summary>
        [Test]
        public static void FlushConsumesPendingDayEvidence()
        {
            int day = CurrentDayIndex();
            string dayKey = DaySummaryKey(pawn, day);
            SeedPendingDayHediff(day);
            PawnDiaryRimTestScope.Require(
                PendingDayHediffContains(dayKey),
                "Test setup failed to seed the pending day-evidence store.");

            scope.FireAndRequireEvent(
                () => InvokeFlush(pawn),
                "DayReflection",
                pawn,
                null);

            PawnDiaryRimTestScope.Require(
                !PendingDayHediffContains(dayKey),
                "The flush did not consume the pending day-evidence batch for this pawn/day.");
        }

        /// <summary>
        /// Frequency policy may deliberately skip the selected page, but that still settles today's
        /// reflection opportunity. Its evidence, once-per-day guard, and shared cadence advance exactly
        /// once, while the flush truthfully reports no new page to its caller.
        /// </summary>
        [Test]
        public static void FrequencyRejectedDayReflectionSettlesWithoutRetryingOrWritingPage()
        {
            int day = CurrentDayIndex();
            int nowTick = Find.TickManager.TicksGame;
            string dayKey = DaySummaryKey(pawn, day);
            ForceFrequencyRejection("dayreflection");
            SeedPendingDayHediff(day);

            scope.RequireNoNewEvent(() => InvokeFlush(pawn));

            PawnReflectionState state = DiaryRecord().EnsureReflectionState();
            PawnDiaryRimTestScope.Require(
                !PendingDayHediffContains(dayKey)
                    && GuardSetContains("writtenDayReflections", dayKey),
                "A frequency-rejected day reflection did not consume its evidence and close its day guard.");
            PawnDiaryRimTestScope.Require(
                state.lastDayTick == nowTick && state.lastReflectionTick == nowTick,
                "A frequency-rejected day reflection did not advance its kind and global cadence ticks.");

            // A later scan must observe the closed opportunity, not reroll it. Fresh same-day evidence is
            // deliberately left alone because the already-written guard returns before collection.
            SeedPendingDayHediff(day);
            scope.RequireNoNewEvent(() => InvokeFlush(pawn));
            PawnDiaryRimTestScope.Require(
                PendingDayHediffContains(dayKey),
                "A settled frequency skip retried and consumed fresh evidence from the same day.");
        }

        /// <summary>
        /// N4. A disabled Reflection group advances the current day window without creating a page.
        /// Re-enabling in that same window cannot dump the evidence accumulated while disabled.
        /// </summary>
        [Test]
        public static void DisabledReflectionGroupBoundsDayDebtAcrossReenable()
        {
            int day = CurrentDayIndex();
            string dayKey = DaySummaryKey(pawn, day);
            SeedPendingDayHediff(day);

            PawnDiaryMod.Settings.SetGroupEnabled("reflection", false);
            scope.RequireNoNewEvent(() => InvokeFlush(pawn));
            PawnDiaryRimTestScope.Require(
                !PendingDayHediffContains(dayKey),
                "A disabled day-reflection group should consume/bound the current day evidence debt.");

            PawnDiaryMod.Settings.SetGroupEnabled("reflection", true);
            SeedPendingDayHediff(day);
            scope.RequireNoNewEvent(() => InvokeFlush(pawn));
            PawnDiaryRimTestScope.Require(
                PendingDayHediffContains(dayKey),
                "Re-enabling should not bypass the day guard or consume fresh evidence without a page.");
        }

        /// <summary>
        /// N4 review regression. Turning day summaries off must preserve ambient notes so the production
        /// sleep path can fall through to its documented per-source ambient-note writers.
        /// </summary>
        [Test]
        public static void DisabledDaySummaryPreservesAmbientFallbackEvidence()
        {
            int day = CurrentDayIndex();
            string ambientKey = SeedPendingAmbientInteraction(day);
            RegisterAmbientFallbackCleanup(ambientKey);
            tuning.daySummaryEnabled = false;
            tuning.quadrumReflectionEnabled = false;
            tuning.arcReflectionEnabled = false;

            scope.RequireNoNewEvent(() => InvokeFlush(pawn));

            PawnDiaryRimTestScope.Require(
                PendingAmbientInteractionNotes().Contains(ambientKey),
                "Disabled day-summary debt bounding must leave the ambient note for the fallback writer.");
            PawnDiaryRimTestScope.Require(
                !WrittenAmbientInteractionContains(ambientKey),
                "Debt bounding must not falsely mark an ambient note written before the fallback emits it.");
        }

        /// <summary>
        /// N4 review regression. The silent additive-state baseline must also preserve newly accumulated
        /// ambient notes when day summaries are disabled; those notes are not historical reflection debt.
        /// </summary>
        [Test]
        public static void OldSaveBaselinePreservesDisabledDaySummaryFallback()
        {
            int day = CurrentDayIndex();
            string ambientKey = SeedPendingAmbientInteraction(day);
            RegisterAmbientFallbackCleanup(ambientKey);
            DiaryRecord().reflectionState = new PawnReflectionState();
            tuning.daySummaryEnabled = false;
            tuning.quadrumReflectionEnabled = false;
            tuning.arcReflectionEnabled = false;

            scope.RequireNoNewEvent(() => InvokeFlush(pawn));

            PawnDiaryRimTestScope.Require(
                PendingAmbientInteractionNotes().Contains(ambientKey),
                "The old-save baseline must preserve ambient evidence owned by the disabled-summary fallback.");
            PawnDiaryRimTestScope.Require(
                !WrittenAmbientInteractionContains(ambientKey),
                "The old-save baseline must not mark preserved fallback evidence as written.");
        }

        /// <summary>
        /// N4. A record that lacks the additive runtime row baselines its current cadence window silently.
        /// Repeated scans in that same rest/day cannot turn the pre-upgrade evidence into a catch-up page.
        /// </summary>
        [Test]
        public static void OldSaveReflectionStateBaselinesSilently()
        {
            int day = CurrentDayIndex();
            string dayKey = DaySummaryKey(pawn, day);
            PawnDiaryRecord diary = DiaryRecord();
            diary.reflectionState = new PawnReflectionState();
            SeedPendingDayHediff(day);

            scope.RequireNoNewEvent(() => InvokeFlush(pawn));
            PawnDiaryRimTestScope.Require(
                !diary.reflectionState.baselineOnNextOpportunity
                    && diary.reflectionState.lastReflectionTick >= 0,
                "The first N4 opportunity should establish a silent cooldown baseline.");
            PawnDiaryRimTestScope.Require(
                !PendingDayHediffContains(dayKey),
                "The silent baseline should bound pre-upgrade day evidence.");

            SeedPendingDayHediff(day);
            scope.RequireNoNewEvent(() => InvokeFlush(pawn));
            PawnDiaryRimTestScope.Require(
                PendingDayHediffContains(dayKey),
                "A repeated scan in the baselined window should remain idempotent.");
        }

        /// <summary>
        /// Quality Wave H3. A post-arrival positive letter can become the sole important day signal
        /// when XML promotes news, and the persisted reflection context records its stable category.
        /// </summary>
        [Test]
        public static void PostArrivalLetterAddsCategorizedNewsCandidate()
        {
            int arrivalTick = NewsFixtureTick(2);
            SeedArrivalBoundary(arrivalTick);
            RequireEffectiveArrivalTick(arrivalTick);
            ReserveNewestArchiveSlots(arrivalTick);
            SeedArchiveLetter("PositiveEvent", "A refugee was welcomed", NewsFixtureTick(1));
            tuning.daySummaryImportantSignalKinds = new List<string> { "news" };

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeFlush(pawn),
                "DayReflection",
                pawn,
                null);

            string context = diaryEvent.gameContext ?? string.Empty;
            PawnDiaryRimTestScope.Require(
                context.IndexOf("news:positive", StringComparison.OrdinalIgnoreCase) >= 0,
                "The allowed post-arrival letter did not reach the reflection as news:positive.");
            string evidence = diaryEvent.initiatorText ?? string.Empty;
            PawnDiaryRimTestScope.Require(
                evidence.IndexOf("A refugee was welcomed", StringComparison.OrdinalIgnoreCase) >= 0,
                "The selected archived letter label did not reach the frozen reflection evidence: "
                    + Describe(evidence));
        }

        /// <summary>
        /// Quality Wave H3. Letters before the pawn's arrival boundary and before today's evidence
        /// window stay absent; an independent hediff signal still lets the page expose the omission.
        /// </summary>
        [Test]
        public static void PreArrivalAndExpiredLettersAreExcluded()
        {
            int day = CurrentDayIndex();
            int dayStartTick = GameTickForDay(day);
            SeedArchiveLetter("PositiveEvent", "Yesterday's old colony news", dayStartTick - 1);
            SeedArchiveLetter("PositiveEvent", "News from before joining", NewsFixtureTick(2));
            int arrivalTick = NewsFixtureTick(1);
            SeedArrivalBoundary(arrivalTick);
            RequireEffectiveArrivalTick(arrivalTick);
            SeedPendingDayHediff(day);
            tuning.daySummaryImportantSignalKinds = new List<string> { "hediff" };

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeFlush(pawn),
                "DayReflection",
                pawn,
                null);

            // A letter the developer's own colony filed after this pawn joined is legitimate news, so
            // the contract is about these two specific letters, not about the page being news-free.
            RequireSeededLettersExcluded(
                diaryEvent,
                "Yesterday's old colony news",
                "News from before joining");
        }

        /// <summary>
        /// Quality Wave H3 review regression. A disabled arrival page still leaves durable arrival
        /// knowledge; its captured tick must bound news exactly like a visible hot/archive page.
        /// </summary>
        [Test]
        public static void KnowledgeOnlyArrivalBoundaryExcludesEarlierNews()
        {
            int day = CurrentDayIndex();
            SeedArchiveLetter(
                "PositiveEvent", "News from before the hidden arrival", NewsFixtureTick(2));
            int arrivalTick = NewsFixtureTick(1);
            SeedKnowledgeArrivalBoundary(arrivalTick);
            RequireEffectiveArrivalTick(arrivalTick);
            SeedPendingDayHediff(day);
            tuning.daySummaryImportantSignalKinds = new List<string> { "hediff" };

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeFlush(pawn),
                "DayReflection",
                pawn,
                null);

            RequireSeededLettersExcluded(diaryEvent, "News from before the hidden arrival");
        }

        /// <summary>
        /// Quality Wave H3. A hot direct raid page suppresses the newer threat letter, while an older
        /// positive letter remains eligible instead of the whole colony-news collector going silent.
        /// </summary>
        [Test]
        public static void HotDirectOwnerSuppressesOnlyItsNewsCategory()
        {
            int arrivalTick = NewsFixtureTick(3);
            SeedArrivalBoundary(arrivalTick);
            ReserveNewestArchiveSlots(arrivalTick);
            SeedArchiveLetter("PositiveEvent", "A trader offered welcome news", NewsFixtureTick(2));
            SeedArchiveLetter("ThreatBig", "Raiders were sighted", NewsFixtureTick(1));
            scope.Component.AddSoloEvent(
                pawn,
                null,
                "RaidEnemy",
                "raid",
                "Raiders attacked the colony.",
                "write about the raid",
                "raid=EnemyRaid");
            tuning.daySummaryImportantSignalKinds = new List<string> { "news" };

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeFlush(pawn),
                "DayReflection",
                pawn,
                null);
            string context = diaryEvent.gameContext ?? string.Empty;
            PawnDiaryRimTestScope.Require(
                context.IndexOf("news:positive", StringComparison.OrdinalIgnoreCase) >= 0,
                "Suppressing the owned threat category also hid the unrelated positive news.");
            PawnDiaryRimTestScope.Require(
                context.IndexOf("news:threat", StringComparison.OrdinalIgnoreCase) < 0,
                "The hot direct raid page did not suppress its same-category threat letter.");
        }

        /// <summary>
        /// Quality Wave H3. The same category ownership check includes compact archived diary rows,
        /// not only hot DiaryEvents.
        /// </summary>
        [Test]
        public static void ArchivedDirectOwnerSuppressesSameCategoryNews()
        {
            int arrivalTick = NewsFixtureTick(4);
            SeedArrivalBoundary(arrivalTick);
            ReserveNewestArchiveSlots(arrivalTick);
            SeedArchiveLetter("PositiveEvent", "A calm opportunity appeared", NewsFixtureTick(3));
            SeedArchiveLetter("ThreatBig", "A mech threat was announced", NewsFixtureTick(2));
            SeedArchivedDirectOwner(
                "archived-raid-owner-" + Guid.NewGuid().ToString("N"),
                NewsFixtureTick(1),
                "Raid",
                "raid=MechCluster");
            tuning.daySummaryImportantSignalKinds = new List<string> { "news" };

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeFlush(pawn),
                "DayReflection",
                pawn,
                null);
            string context = diaryEvent.gameContext ?? string.Empty;
            PawnDiaryRimTestScope.Require(
                context.IndexOf("news:positive", StringComparison.OrdinalIgnoreCase) >= 0,
                "The unrelated positive category should survive archived threat ownership.");
            PawnDiaryRimTestScope.Require(
                context.IndexOf("news:threat", StringComparison.OrdinalIgnoreCase) < 0,
                "The archived direct raid row did not suppress same-category threat news.");
        }

        /// <summary>
        /// Quality Wave H3 review regression. Direct ownership is same-category and same-day: an older
        /// progression page must not hide a distinct positive letter from the next quadrum day.
        /// </summary>
        [Test]
        public static void DifferentDayDirectOwnerDoesNotSuppressQuadrumNews()
        {
            int firstDayStart = GameTickForDay(CurrentDayIndex());
            int secondDayStart = firstDayStart + GenDate.TicksPerDay;
            SeedArrivalBoundary(firstDayStart);
            SeedArchivedDirectOwner(
                "archived-positive-owner-" + Guid.NewGuid().ToString("N"),
                firstDayStart + 10,
                DiaryEventDomainClassifier.Progression,
                "progression=skill");
            SeedArchiveLetter(
                "PositiveEvent",
                "A distinct opportunity arrived the next day",
                secondDayStart + 10);

            List<DiaryGameComponent.QuadrumReflectionSignal> signals =
                CollectQuadrumNews(firstDayStart, secondDayStart + 20);

            PawnDiaryRimTestScope.Require(
                signals.Count == 1
                    && signals[0].contextTag.IndexOf(
                        "news:positive", StringComparison.OrdinalIgnoreCase) >= 0,
                "A previous-day direct owner incorrectly suppressed next-day positive colony news.");
        }

        /// <summary>
        /// Quality Wave H5-prompt. A compact archive-only page from the matching quadrum last year
        /// reaches the production collector as one low-weight memory signal.
        /// </summary>
        [Test]
        public static void ArchiveOnlySameSeasonPageAddsMemorySignal()
        {
            // The callback looks a whole year back, so today's absolute tick is the only anchor these
            // archive rows need — no evidence-window ladder is involved.
            int now = Find.TickManager.TicksGame;
            int currentQuadrum = (CurrentDayIndex() / GenDate.DaysPerQuadrum)
                + QuadrumAnniversaryMemoryPolicy.QuadrumsPerYear;
            string eventId = "quality-wave-anniversary-archive-" + Guid.NewGuid().ToString("N");
            SeedAnniversaryArchive(
                eventId,
                now,
                "A child completed an important growth milestone.",
                DiaryEventDomainClassifier.Progression,
                string.Empty);
            SeedAnniversaryArchive(
                "quality-wave-anniversary-reflection-" + Guid.NewGuid().ToString("N"),
                now,
                "A summary must never become its own memory source.",
                DiaryEventDomainClassifier.Reflection,
                "quadrum_reflection=true",
                DayReflectionEventData.QuadrumDefNameToken);

            List<DiaryGameComponent.QuadrumReflectionSignal> signals =
                new List<DiaryGameComponent.QuadrumReflectionSignal>();
            scope.Component.CollectLastYearQuadrumMemory(
                pawn.GetUniqueLoadID(), currentQuadrum, signals);

            PawnDiaryRimTestScope.Require(
                signals.Count == 1,
                "The archive-only same-season page did not produce exactly one callback signal.");
            PawnDiaryRimTestScope.Require(
                signals[0].contextTag.IndexOf(
                    DayReflectionEventData.SignalKindMemory + ":" + eventId,
                    StringComparison.Ordinal) >= 0,
                "The archive-only callback did not carry its stable memory identity.");
            PawnDiaryRimTestScope.Require(
                signals[0].evidenceLine.IndexOf(
                    "A child completed an important growth milestone.",
                    StringComparison.Ordinal) >= 0,
                "The archive-only callback did not carry the frozen archive evidence.");
            PawnDiaryRimTestScope.Require(
                Math.Abs(signals[0].weight - (tuning.daySummaryWeightMajorEvent * 0.5f)) < 0.0001f,
                "The callback did not use half the major-event reflection weight.");
        }

        /// <summary>
        /// Quality Wave H5-prompt. One event present in both hot and archived storage remains one
        /// callback, and a first-year quadrum never emits a prior-year memory.
        /// </summary>
        [Test]
        public static void HotArchiveOverlapDeduplicatesAndFirstYearStaysEmpty()
        {
            int now = Find.TickManager.TicksGame;
            DiaryEvent hot = scope.Component.AddSoloEvent(
                pawn,
                null,
                "RaidEnemy",
                "raid",
                "Raiders attacked the colony.",
                "write about the raid",
                "raid=EnemyRaid",
                now);
            PawnDiaryRimTestScope.Require(
                hot != null && hot.IsImportant(),
                "Could not seed an important hot page for the H5 overlap fixture.");
            SeedAnniversaryArchive(
                hot.eventId,
                hot.tick,
                "Archived duplicate of the same raid.",
                DiaryEventDomainClassifier.Raid,
                "raid=EnemyRaid");

            int callbackQuadrum = (CurrentDayIndex() / GenDate.DaysPerQuadrum)
                + QuadrumAnniversaryMemoryPolicy.QuadrumsPerYear;
            List<DiaryGameComponent.QuadrumReflectionSignal> signals =
                new List<DiaryGameComponent.QuadrumReflectionSignal>();
            scope.Component.CollectLastYearQuadrumMemory(
                pawn.GetUniqueLoadID(), callbackQuadrum, signals);
            PawnDiaryRimTestScope.Require(
                signals.Count == 1,
                "The hot/archive representation of one event produced duplicate callback signals.");
            PawnDiaryRimTestScope.Require(
                signals[0].contextTag.IndexOf(hot.eventId, StringComparison.Ordinal) >= 0,
                "The deduplicated callback lost the shared event identity.");

            signals.Clear();
            scope.Component.CollectLastYearQuadrumMemory(pawn.GetUniqueLoadID(), 3, signals);
            PawnDiaryRimTestScope.Require(
                signals.Count == 0,
                "A first-year quadrum emitted an impossible prior-year callback.");
        }

        /// <summary>
        /// Quality Wave Phase 3 review regression. A prior-year callback is auxiliary context, so five
        /// current important pages cannot satisfy the shipped six-entry quadrum gate.
        /// </summary>
        [Test]
        public static void PriorYearCallbackCannotSatisfyCurrentQuadrumEntryGate()
        {
            int currentDay = CurrentDayIndex();
            int currentQuadrum = (currentDay / GenDate.DaysPerQuadrum)
                + QuadrumAnniversaryMemoryPolicy.QuadrumsPerYear;
            int quadrumStartDay = currentQuadrum * GenDate.DaysPerQuadrum;
            int dueDay = quadrumStartDay + QuadrumReflectionPolicy.DueDayInQuadrum(
                pawn.GetUniqueLoadID(),
                currentQuadrum,
                GenDate.DaysPerQuadrum,
                tuning.quadrumReflectionTimingWindowDays);

            tuning.quadrumReflectionEnabled = true;
            tuning.onThisDayQuadrumCallbackEnabled = true;
            tuning.quadrumReflectionMinImportantEntries = 6;

            DiaryEvent previousYear = scope.Component.AddSoloEvent(
                pawn,
                null,
                "RaidEnemy",
                "raid",
                "A raid from the matching quadrum last year.",
                "write about the raid",
                "raid=EnemyRaid",
                Find.TickManager.TicksGame);
            PawnDiaryRimTestScope.Require(
                previousYear != null && previousYear.IsImportant(),
                "Could not seed the prior-year auxiliary callback page.");

            for (int i = 0; i < 5; i++)
            {
                DiaryEvent current = scope.Component.AddSoloEvent(
                    pawn,
                    null,
                    "RaidEnemy",
                    "raid",
                    "Current-quadrum important event " + i,
                    "write about the raid",
                    "raid=EnemyRaid",
                    GameTickForDay(quadrumStartDay) + 100 + i);
                PawnDiaryRimTestScope.Require(
                    current != null && current.IsImportant(),
                    "Could not seed current-quadrum important event " + i + ".");
            }

            ReflectionOpportunity opportunity = PrepareQuadrumOpportunity(pawn, dueDay);
            PawnDiaryRimTestScope.Require(
                opportunity != null && !opportunity.due,
                "The prior-year callback incorrectly lowered the six-current-entry quadrum gate.");
            PawnDiaryRimTestScope.Require(
                opportunity.candidateMemoryCount == 5,
                "Auxiliary callback candidates inflated quadrum arbitration evidence.");
        }

        /// <summary>
        /// Quality Wave Phase 3 review regression. Both XML-backed feature controls must be reachable
        /// through the Advanced settings catalog instead of requiring a hand edit.
        /// </summary>
        [Test]
        public static void Phase3TuningFieldsAreAdvancedEditable()
        {
            PawnDiaryRimTestScope.Require(
                AdvancedCatalogContains("daySummaryWeightNews"),
                "Advanced settings omitted the H3 colony-news weight.");
            PawnDiaryRimTestScope.Require(
                AdvancedCatalogContains("onThisDayQuadrumCallbackEnabled"),
                "Advanced settings omitted the H5 callback toggle.");
        }

        // ----- production seam invocation ---------------------------------------------------------

        // Invokes the retained private dev/test seam, which delegates to the same per-pawn arbitration
        // used by FlushAmbientNotesForSleepingPawns while avoiding storyteller/rest scheduling.
        private static void InvokeFlush(Pawn target)
        {
            MethodInfo flush = typeof(DiaryGameComponent).GetMethod("FlushDaySummaryForPawn", NonPublicInstance);
            if (flush == null)
            {
                throw new AssertionException(
                    "EVT-19 could not locate DiaryGameComponent.FlushDaySummaryForPawn(Pawn).");
            }

            flush.Invoke(scope.Component, new object[] { target });
        }

        private static List<DiaryGameComponent.QuadrumReflectionSignal> CollectQuadrumNews(
            int startTick,
            int endTick)
        {
            Type signalList = typeof(List<DiaryGameComponent.QuadrumReflectionSignal>);
            MethodInfo method = typeof(DiaryGameComponent).GetMethod(
                "CollectNewsSignals",
                NonPublicInstance,
                null,
                new[] { typeof(Pawn), typeof(int), typeof(int), signalList },
                null);
            if (method == null)
            {
                throw new AssertionException(
                    "Could not locate the quadrum CollectNewsSignals production overload.");
            }

            List<DiaryGameComponent.QuadrumReflectionSignal> signals =
                new List<DiaryGameComponent.QuadrumReflectionSignal>();
            method.Invoke(scope.Component, new object[] { pawn, startTick, endTick, signals });
            return signals;
        }

        private static ReflectionOpportunity PrepareQuadrumOpportunity(Pawn target, int day)
        {
            MethodInfo method = typeof(DiaryGameComponent).GetMethod(
                "PrepareQuadrumReflectionCandidate",
                NonPublicInstance);
            if (method == null)
            {
                throw new AssertionException(
                    "Could not locate PrepareQuadrumReflectionCandidate.");
            }

            object runtime = method.Invoke(
                scope.Component,
                new object[] { target, target.GetUniqueLoadID(), day, true });
            FieldInfo field = runtime?.GetType().GetField("opportunity");
            ReflectionOpportunity opportunity = field?.GetValue(runtime) as ReflectionOpportunity;
            if (opportunity == null)
            {
                throw new AssertionException(
                    "Could not inspect the prepared quadrum opportunity.");
            }

            return opportunity;
        }

        private static PawnDiaryRecord DiaryRecord()
        {
            MethodInfo method = typeof(DiaryGameComponent).GetMethod("FindDiary", NonPublicInstance);
            PawnDiaryRecord diary = method?.Invoke(scope.Component, new object[] { pawn, true })
                as PawnDiaryRecord;
            if (diary == null)
            {
                throw new AssertionException("Could not resolve the test pawn's diary record.");
            }

            return diary;
        }

        // ----- Quality Wave H3 archived-letter fixtures -------------------------------------------

        // The deepest news ladder below (arrival, two letters, one owning page) needs four ordered
        // ticks inside today's evidence window.
        private const int NewsFixtureTickSpan = 4;

        /// <summary>
        /// Returns a fixture tick <paramref name="stepsBeforeNow"/> ticks below the live clock.
        /// The colony-news collector only cares about ORDER inside the pawn's evidence window, never
        /// about distance, so the ladder is deliberately compact: it works on a colony that started
        /// moments ago as well as on a long save, it stays inside RimWorld's newest-first archive
        /// scan-back cap, and it keeps the developer's own letters out of the fixture's way.
        /// </summary>
        private static int NewsFixtureTick(int stepsBeforeNow)
        {
            int now = Find.TickManager.TicksGame;
            // Below tick 0 an archived letter is unreadable, and before today's start it falls out of
            // the daily evidence window; both would silently void the fixture instead of testing it.
            int earliestUsableTick = Math.Max(0, GameTickForDay(CurrentDayIndex()));
            if (now - earliestUsableTick < NewsFixtureTickSpan)
            {
                throw new AssertionException(
                    "Quality Wave H3 reflection fixtures need at least " + NewsFixtureTickSpan
                    + " ticks elapsed in the current in-game day; let the colony run a moment first.");
            }

            return now - stepsBeforeNow;
        }

        /// <summary>
        /// Confirms the production boundary reader really sees the tick this fixture just seeded.
        /// Without it the news window silently widens to the whole day, and every "this letter was
        /// excluded" assertion below becomes a coin flip against the developer's own colony letters —
        /// a confusing failure far from its cause. Fail here instead, naming the tick actually read.
        /// </summary>
        private static void RequireEffectiveArrivalTick(int expected)
        {
            int? actual = EffectiveArrivalTick();
            PawnDiaryRimTestScope.Require(
                actual.HasValue && actual.Value == expected,
                "The seeded arrival boundary did not reach the production reader: expected "
                    + expected + ", read "
                    + (actual.HasValue ? actual.Value.ToString() : "none")
                    + " (now=" + Find.TickManager.TicksGame + ").");
        }

        // Reads the same private boundary the H3 collector clips its news window with.
        private static int? EffectiveArrivalTick()
        {
            MethodInfo findDiary = typeof(DiaryGameComponent).GetMethod("FindDiary", NonPublicInstance);
            MethodInfo method = typeof(DiaryGameComponent).GetMethod(
                "FirstArrivalTickFor", NonPublicInstance);
            if (findDiary == null || method == null)
            {
                throw new AssertionException(
                    "Could not locate DiaryGameComponent.FindDiary/FirstArrivalTickFor.");
            }

            object diary = findDiary.Invoke(scope.Component, new object[] { pawn, false });
            object result = method.Invoke(
                scope.Component, new object[] { pawn.GetUniqueLoadID(), diary });
            return result == null ? (int?)null : (int)result;
        }

        /// <summary>
        /// Asserts none of the named seeded letters reached the page's frozen evidence. Deliberately
        /// checks those exact labels rather than the absence of any news: a letter the developer's own
        /// colony filed after this pawn joined is correct news, not a leak. The seeded hediff evidence
        /// is checked first, so an empty evidence field can never make the negative assertion vacuous.
        /// </summary>
        private static void RequireSeededLettersExcluded(
            DiaryEvent diaryEvent,
            params string[] labels)
        {
            string evidence = diaryEvent.initiatorText ?? string.Empty;
            PawnDiaryRimTestScope.Require(
                evidence.IndexOf("test affliction", StringComparison.OrdinalIgnoreCase) >= 0,
                "The page carried no seeded hediff evidence, so the exclusion check is vacuous: "
                    + Describe(evidence));
            for (int i = 0; i < labels.Length; i++)
            {
                PawnDiaryRimTestScope.Require(
                    evidence.IndexOf(labels[i], StringComparison.OrdinalIgnoreCase) < 0,
                    "An out-of-window archived letter leaked into the day reflection: '"
                        + labels[i] + "' in " + Describe(evidence));
            }
        }

        // Compacts a multi-line evidence body into one readable line for an assertion message.
        private static string Describe(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "<empty>";
            }

            return "\"" + value.Replace("\n", " / ") + "\"";
        }

        /// <summary>
        /// Lifts every already-archived row at or after <paramref name="fromTick"/> out of RimWorld's
        /// archive for the rest of this test, then puts it back in teardown.
        ///
        /// The game clock does not advance while the test runner works, so rows raised by earlier suites
        /// are archived at exactly the current TicksGame. The production collector's bounded scan counts
        /// every archive row, even non-letters, before it type-narrows. Enough same-tick rows can therefore
        /// hide the fixture letter beyond the scan cap on a repeat run. Pinned rows are left alone: those
        /// are a deliberate player action, and one pinned at this exact tick is not a case worth trading
        /// save fidelity for.
        /// </summary>
        private static void ReserveNewestArchiveSlots(int fromTick)
        {
            if (Find.Archive == null)
            {
                return;
            }

            List<IArchivable> displaced = new List<IArchivable>();
            IReadOnlyList<IArchivable> archivables = Find.Archive.ArchivablesListForReading;
            for (int i = 0; i < archivables.Count; i++)
            {
                IArchivable archivable = archivables[i];
                int createdTick;
                try
                {
                    createdTick = archivable?.CreatedTicksGame ?? -1;
                }
                catch
                {
                    continue;
                }

                if (createdTick >= fromTick && !Find.Archive.IsPinned(archivable))
                {
                    displaced.Add(archivable);
                }
            }

            for (int i = 0; i < displaced.Count; i++)
            {
                Find.Archive.Remove(displaced[i]);
            }

            scope.RegisterCleanup(() =>
            {
                for (int i = 0; i < displaced.Count; i++)
                {
                    Find.Archive?.Add(displaced[i]);
                }
            });
        }

        /// <summary>
        /// Turns the XML-backed colony-news collector off for the rest of this test, restoring the
        /// shipped value in teardown. Used by fixtures whose assertions count candidates: news is the
        /// one collector that reads live colony state the fixture cannot own.
        /// </summary>
        private static void SuppressColonyNewsForThisTest()
        {
            DiaryContextReactionDef policy =
                DiaryContextReactions.ForKey(DiaryContextReactions.ColonyNews);
            if (policy == null)
            {
                throw new AssertionException("Could not resolve the colony-news reaction policy.");
            }

            bool saved = policy.enabled;
            policy.enabled = false;
            scope.RegisterCleanup(() => policy.enabled = saved);
        }

        private static int GameTickForDay(int day)
        {
            int offset = Find.TickManager.TicksAbs - Find.TickManager.TicksGame;
            return (day * GenDate.TicksPerDay) - offset;
        }

        private static void SeedArrivalBoundary(int tick)
        {
            ArchivedDiaryEntry entry = new ArchivedDiaryEntry
            {
                eventId = "quality-wave-arrival-" + Guid.NewGuid().ToString("N"),
                pawnId = pawn.GetUniqueLoadID(),
                povRole = DiaryEvent.NeutralRole,
                tick = tick,
                text = "test arrival boundary",
                interactionDefName = "PawnDiary_Arrival",
                arrivalDescription = true
            };
            PawnDiaryRimTestScope.Require(
                ArchiveRepository().AddOrKeep(entry),
                "Could not seed the H3 archived arrival boundary.");
        }

        private static void SeedKnowledgeArrivalBoundary(int tick)
        {
            PawnKnowledgeState knowledge = DiaryRecord().EnsureKnowledgeState();
            string id = "quality-wave-arrival-knowledge-" + Guid.NewGuid().ToString("N");
            knowledge.records.Add(new ImportantMemoryRecord
            {
                recordId = id,
                dedupKey = id,
                eventKind = KnowledgeTokens.EventKindFactionJoined,
                tick = tick,
                fallbackSummary = "test knowledge-only arrival boundary"
            });
        }

        private static void SeedArchivedDirectOwner(
            string eventId,
            int tick,
            string domain,
            string gameContext)
        {
            ArchivedDiaryEntry entry = new ArchivedDiaryEntry
            {
                eventId = eventId,
                pawnId = pawn.GetUniqueLoadID(),
                povRole = DiaryEvent.InitiatorRole,
                tick = tick,
                text = "test direct owner",
                interactionDefName = "QualityWaveDirectOwner",
                important = true,
                decorationDomain = domain,
                decorationGameContext = gameContext
            };
            PawnDiaryRimTestScope.Require(
                ArchiveRepository().AddOrKeep(entry),
                "Could not seed the H3 archived direct-story owner.");
        }

        private static void SeedAnniversaryArchive(
            string eventId,
            int tick,
            string text,
            string domain,
            string gameContext,
            string interactionDefName = "QualityWaveAnniversaryMemory")
        {
            ArchivedDiaryEntry entry = new ArchivedDiaryEntry
            {
                eventId = eventId,
                pawnId = pawn.GetUniqueLoadID(),
                povRole = DiaryEvent.InitiatorRole,
                tick = tick,
                date = "test date",
                text = text,
                interactionDefName = interactionDefName,
                interactionLabel = "test anniversary memory",
                important = true,
                decorationDomain = domain,
                decorationGameContext = gameContext
            };
            PawnDiaryRimTestScope.Require(
                ArchiveRepository().AddOrKeep(entry),
                "Could not seed the H5 archived anniversary memory.");
            scope.RegisterCleanup(() => ArchiveRepository().RemoveForEventIds(
                new HashSet<string> { eventId }));
        }

        private static DiaryArchiveRepository ArchiveRepository()
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField("archive", NonPublicInstance);
            DiaryArchiveRepository repository = field?.GetValue(scope.Component) as DiaryArchiveRepository;
            if (repository == null)
            {
                throw new AssertionException("Could not read DiaryGameComponent.archive.");
            }

            return repository;
        }

        private static void SeedArchiveLetter(string letterDefName, string label, int tick)
        {
            LetterDef letterDef = DefDatabase<LetterDef>.GetNamedSilentFail(letterDefName);
            if (letterDef == null)
            {
                throw new AssertionException(
                    "Required LetterDef '" + letterDefName + "' is not loaded.");
            }

            Letter letter = LetterMaker.MakeLetter(letterDef);
            letter.Label = label;
            letter.arrivalTick = tick;
            PawnDiaryRimTestScope.Require(
                Find.Archive.Add(letter),
                "Could not seed archived letter '" + letterDefName + "'.");
            scope.RegisterCleanup(() => Find.Archive?.Remove(letter));
        }

        private static bool AdvancedCatalogContains(string fieldName)
        {
            IReadOnlyList<AdvancedFieldDescriptor> fields = AdvancedFieldCatalog.All;
            for (int i = 0; i < fields.Count; i++)
            {
                if (string.Equals(fields[i]?.fieldName, fieldName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // Ordinary EVT-19 tests seed current-day evidence and expect to exercise selection immediately.
        // N4's default-true flags instead model an upgraded save's first rest, so clear both boundaries
        // here. The two old-save tests below replace reflectionState after setup and therefore still drive
        // the real silent-baseline branches explicitly.
        private static void MarkOrdinaryFixtureAsAlreadyBaselined()
        {
            PawnReflectionState state = DiaryRecord().EnsureReflectionState();
            state.baselineOnNextOpportunity = false;
            state.linkedBaselineOnNextOpportunity = false;
        }

        // ----- day-evidence seeding ---------------------------------------------------------------

        // Adds one important affliction record to pendingDayHediffs["pawnId|day"] — the same store the
        // live Pawn_HealthTracker.AddHediff hook fills for day-reflection-mode hediffs. One record is a
        // single important candidate, which forces the otherwise weighted-random highlight selection.
        private static void SeedPendingDayHediff(int day)
        {
            IDictionary pending = PendingDayHediffs();
            string dayKey = DaySummaryKey(pawn, day);

            Type recordType = typeof(DiaryGameComponent).GetNestedType("DayHediffRecord", BindingFlags.NonPublic);
            if (recordType == null)
            {
                throw new AssertionException(
                    "EVT-19 could not locate the private DiaryGameComponent.DayHediffRecord struct.");
            }

            object record = Activator.CreateInstance(recordType);
            SetRecordField(recordType, record, "defName", SeedHediffDefName);
            SetRecordField(recordType, record, "label", "test affliction");
            SetRecordField(recordType, record, "weight", 1f);
            SetRecordField(recordType, record, "progressed", false);

            Type listType = typeof(List<>).MakeGenericType(recordType);
            object list = Activator.CreateInstance(listType);
            listType.GetMethod("Add").Invoke(list, new[] { record });

            pending[dayKey] = list;
        }

        private static void SetRecordField(Type recordType, object boxedRecord, string fieldName, object value)
        {
            FieldInfo field = recordType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
            {
                throw new AssertionException(
                    "EVT-19 could not set DayHediffRecord field '" + fieldName + "' (renamed?).");
            }

            field.SetValue(boxedRecord, value);
        }

        private static bool PendingDayHediffContains(string dayKey)
        {
            return PendingDayHediffs().Contains(dayKey);
        }

        private static IDictionary PendingDayHediffs()
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField("pendingDayHediffs", NonPublicInstance);
            IDictionary dictionary = field?.GetValue(scope.Component) as IDictionary;
            if (dictionary == null)
            {
                throw new AssertionException(
                    "EVT-19 could not read DiaryGameComponent.pendingDayHediffs.");
            }

            return dictionary;
        }

        private static string SeedPendingAmbientInteraction(int day)
        {
            const string keyPrefix = "review-day-fallback|";
            string key = keyPrefix + day + "|" + pawn.GetUniqueLoadID();
            Type noteType = typeof(DiaryGameComponent).GetNestedType(
                "PendingAmbientInteractionNote", BindingFlags.NonPublic);
            if (noteType == null)
            {
                throw new AssertionException(
                    "Could not locate DiaryGameComponent.PendingAmbientInteractionNote.");
            }

            object note = Activator.CreateInstance(noteType);
            SetRecordField(noteType, note, "key", key);
            SetRecordField(noteType, note, "pawn", pawn);
            SetRecordField(noteType, note, "pawnId", pawn.GetUniqueLoadID());
            SetRecordField(noteType, note, "dayIndex", day);
            SetRecordField(noteType, note, "eventCount", 1);
            PendingAmbientInteractionNotes()[key] = note;
            return key;
        }

        private static IDictionary PendingAmbientInteractionNotes()
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(
                "pendingAmbientInteractionNotes", NonPublicInstance);
            IDictionary dictionary = field?.GetValue(scope.Component) as IDictionary;
            if (dictionary == null)
            {
                throw new AssertionException(
                    "Could not read DiaryGameComponent.pendingAmbientInteractionNotes.");
            }

            return dictionary;
        }

        private static bool WrittenAmbientInteractionContains(string key)
        {
            object written = WrittenAmbientInteractionNotes();
            MethodInfo contains = written.GetType().GetMethod("Contains", new[] { typeof(string) });
            return contains != null && (bool)contains.Invoke(written, new object[] { key });
        }

        private static void RegisterAmbientFallbackCleanup(string key)
        {
            scope.RegisterCleanup(() =>
            {
                PendingAmbientInteractionNotes().Remove(key);
                object written = WrittenAmbientInteractionNotes();
                written.GetType().GetMethod("Remove", new[] { typeof(string) })
                    ?.Invoke(written, new object[] { key });
            });
        }

        private static object WrittenAmbientInteractionNotes()
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(
                "writtenAmbientInteractionNotes", NonPublicInstance);
            object written = field?.GetValue(scope.Component);
            if (written == null)
            {
                throw new AssertionException(
                    "Could not read DiaryGameComponent.writtenAmbientInteractionNotes.");
            }

            return written;
        }

        // Matches DiaryGameComponent.DaySummaryKey(pawnId, day): "pawnId|day".
        private static string DaySummaryKey(Pawn target, int day)
        {
            return target.GetUniqueLoadID() + "|" + day;
        }

        // Matches DiaryGameComponent.CurrentDayIndex: absolute in-game day from the world clock.
        private static int CurrentDayIndex()
        {
            return Find.TickManager.TicksAbs / GenDate.TicksPerDay;
        }

        // ----- deterministic tuning + guard cleanup -----------------------------------------------

        // Forces the reflection tuning so the daily flush is the only reflection that can fire and the
        // seeded hediff counts as an important candidate. Restored in teardown via RegisterCleanup.
        private static void ForceDeterministicReflectionTuning()
        {
            tuning = DiaryTuning.Current;
            savedDaySummaryEnabled = tuning.daySummaryEnabled;
            savedQuadrumEnabled = tuning.quadrumReflectionEnabled;
            savedQuadrumCallbackEnabled = tuning.onThisDayQuadrumCallbackEnabled;
            savedArcEnabled = tuning.arcReflectionEnabled;
            savedImportantKinds = tuning.daySummaryImportantSignalKinds;
            savedQuadrumMinImportantEntries = tuning.quadrumReflectionMinImportantEntries;

            tuning.daySummaryEnabled = true;
            // Disable the rarer arc/quadrum reflections so they never pre-empt the daily one; the flush
            // tries both before the ordinary day path.
            tuning.quadrumReflectionEnabled = false;
            tuning.onThisDayQuadrumCallbackEnabled = true;
            tuning.arcReflectionEnabled = false;
            // Only "hediff" needs to justify a reflection for these tests; a fresh list leaves the
            // shipped default untouched (the original reference is restored on teardown).
            tuning.daySummaryImportantSignalKinds = new List<string> { "hediff" };

            scope.RegisterCleanup(RestoreReflectionTuning);
        }

        private static void RestoreReflectionTuning()
        {
            if (tuning == null)
            {
                return;
            }

            tuning.daySummaryEnabled = savedDaySummaryEnabled;
            tuning.quadrumReflectionEnabled = savedQuadrumEnabled;
            tuning.onThisDayQuadrumCallbackEnabled = savedQuadrumCallbackEnabled;
            tuning.arcReflectionEnabled = savedArcEnabled;
            tuning.daySummaryImportantSignalKinds = savedImportantKinds;
            tuning.quadrumReflectionMinImportantEntries = savedQuadrumMinImportantEntries;
        }

        // The once-per-day / once-per-quadrum guards (writtenDayReflections / writtenQuadrumReflections)
        // are transient HashSet<string> stores keyed by "pawnId|day" and "pawnId|quadrum". They are not
        // part of the harness's owned/audited set, so scrub the test pawn's keys ourselves to keep the
        // developer's loaded colony clean.
        private static void RegisterReflectionGuardCleanup()
        {
            string pawnId = pawn.GetUniqueLoadID();
            scope.RegisterCleanup(() =>
            {
                ScrubGuardSet("writtenDayReflections", pawnId);
                ScrubGuardSet("writtenQuadrumReflections", pawnId);
            });
        }

        private static void ScrubGuardSet(string fieldName, string pawnId)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(fieldName, NonPublicInstance);
            HashSet<string> set = field?.GetValue(scope.Component) as HashSet<string>;
            set?.RemoveWhere(key => key != null && key.IndexOf(pawnId, StringComparison.Ordinal) >= 0);
        }

        private static bool GuardSetContains(string fieldName, string key)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(fieldName, NonPublicInstance);
            HashSet<string> set = field?.GetValue(scope.Component) as HashSet<string>;
            return set != null && set.Contains(key);
        }

        private static void ForceFrequencyRejection(string groupKey)
        {
            DiaryInteractionGroupDef group = InteractionGroups.ByKey(groupKey);
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (group == null || settings?.groupFrequencyOverrides == null)
            {
                throw new AssertionException(
                    "Could not configure the reflection frequency-rejection fixture for '"
                    + groupKey + "'.");
            }

            float savedValue;
            bool hadSavedValue = settings.groupFrequencyOverrides.TryGetValue(
                group.defName, out savedValue);
            settings.groupFrequencyOverrides[group.defName] = 0f;
            scope.RegisterCleanup(() =>
            {
                if (hadSavedValue)
                {
                    settings.groupFrequencyOverrides[group.defName] = savedValue;
                }
                else
                {
                    settings.groupFrequencyOverrides.Remove(group.defName);
                }
            });
        }
    }
}

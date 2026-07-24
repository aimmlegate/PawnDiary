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
// Determinism: the flush's highlight selection is weighted-random, so each test seeds exactly ONE
// important candidate — with a single candidate the selection is forced (the only element is always
// drawn), so the emitted page and its context are fully deterministic. The XML-backed reflection
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
            int now = RequireUsableCurrentTick();
            SeedArrivalBoundary(now - 100);
            SeedArchiveLetter("PositiveEvent", "A refugee was welcomed", now - 20);
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
            PawnDiaryRimTestScope.Require(
                (diaryEvent.initiatorText ?? string.Empty)
                    .IndexOf("A refugee was welcomed", StringComparison.OrdinalIgnoreCase) >= 0,
                "The selected archived letter label did not reach the frozen reflection evidence.");
        }

        /// <summary>
        /// Quality Wave H3. Letters before the pawn's arrival boundary and before today's evidence
        /// window stay absent; an independent hediff signal still lets the page expose the omission.
        /// </summary>
        [Test]
        public static void PreArrivalAndExpiredLettersAreExcluded()
        {
            int now = RequireUsableCurrentTick();
            int day = CurrentDayIndex();
            int dayStartTick = GameTickForDay(day);
            SeedArchiveLetter("PositiveEvent", "Yesterday's old colony news", dayStartTick - 1);
            SeedArchiveLetter("PositiveEvent", "News from before joining", now - 20);
            SeedArrivalBoundary(now - 10);
            SeedPendingDayHediff(day);
            tuning.daySummaryImportantSignalKinds = new List<string> { "hediff" };

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeFlush(pawn),
                "DayReflection",
                pawn,
                null);

            PawnDiaryRimTestScope.Require(
                (diaryEvent.gameContext ?? string.Empty)
                    .IndexOf("news:", StringComparison.OrdinalIgnoreCase) < 0,
                "A pre-arrival or expired archived letter leaked into the day reflection.");
        }

        /// <summary>
        /// Quality Wave H3. A hot direct raid page suppresses the newer threat letter, while an older
        /// positive letter remains eligible instead of the whole colony-news collector going silent.
        /// </summary>
        [Test]
        public static void HotDirectOwnerSuppressesOnlyItsNewsCategory()
        {
            int now = RequireUsableCurrentTick();
            SeedArrivalBoundary(now - 100);
            SeedArchiveLetter("PositiveEvent", "A trader offered welcome news", now - 20);
            SeedArchiveLetter("ThreatBig", "Raiders were sighted", now - 10);
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
            int now = RequireUsableCurrentTick();
            SeedArrivalBoundary(now - 100);
            SeedArchiveLetter("PositiveEvent", "A calm opportunity appeared", now - 20);
            SeedArchiveLetter("ThreatBig", "A mech threat was announced", now - 10);
            SeedArchivedDirectOwner(
                "archived-raid-owner-" + Guid.NewGuid().ToString("N"),
                now - 5,
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
        /// Quality Wave H5-prompt. A compact archive-only page from the matching quadrum last year
        /// reaches the production collector as one low-weight memory signal.
        /// </summary>
        [Test]
        public static void ArchiveOnlySameSeasonPageAddsMemorySignal()
        {
            int now = RequireUsableCurrentTick();
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
            int now = RequireUsableCurrentTick();
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

        private static int RequireUsableCurrentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now < 200)
            {
                throw new AssertionException(
                    "Quality Wave H3 reflection fixtures require a loaded game beyond tick 200.");
            }

            return now;
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
    }
}

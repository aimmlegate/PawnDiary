// Loaded-game flow for anniversaries and personal records (Quality Wave §8, H2).
//
// The RULES — elapsed-year arithmetic, the milestone set, the grief decay schedule, retention order,
// and monotonic record crossings — are pure and covered headlessly in DiaryPipelineTests. What only a
// loaded game can prove is that the scanner actually turns live pawn state into the right pages:
//   (a) the first scan for a pawn is silent, so an old save receives nothing retroactively;
//   (b) a birthday emits once, and stays silent when a direct birthday page already owns that age;
//   (c) arrival years 1/2/3/5/10/15 emit and 4/6 do not, measured from the pawn's real arrival page;
//   (d) a remembered loss is guaranteed for three years, then decays, and several losses sharing one
//       date produce ONE page naming at most three people;
//   (e) a personal record emits on a real RecordDef crossing and never repeats after a value reset.
//
// Every test drives the production scanner through DiaryGameComponent.ScanAnniversariesForPawn, which
// is the same method the tick loop calls — the only difference is that the harness supplies the "now"
// tick so a whole in-game decade can be tested without advancing the developer's clock.
//
// The fixture suite separately kills an isolated test animal to prove event-time bond capture. These
// flow tests seed saved rows so recall, aggregation, and the once-per-day guard stay deterministic.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Drives the live H2 scanner for all four sub-features plus the first-scan baseline rule.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryAnniversaryFlowTests
    {
        private const string BirthdayGroupKey = "progressionBirthday";
        private const string ArrivalGroupKey = "progressionArrivalAnniversary";
        private const string DeathGroupKey = "progressionDeathAnniversary";
        private const string RecordGroupKey = "progressionRecordMilestone";

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(
                BirthdayGroupKey, ArrivalGroupKey, DeathGroupKey, RecordGroupKey);
            pawn = scope.CreateAdultColonist();
        }

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
        /// The baseline rule: a pawn's first scan records where they already are and writes nothing.
        /// This is what stops an old save from receiving every birthday and milestone it ever passed.
        /// </summary>
        [Test]
        public static void FirstScanBaselinesSilently()
        {
            PawnProgressionState state = State();
            PawnDiaryRimTestScope.Require(state.baselineAnniversariesOnNextScan,
                "A fresh pawn should start with the H2 baseline flag armed.");
            PawnDiaryRimTestScope.Require(state.lastObservedBiologicalAgeYears < 0,
                "A fresh pawn should start with no observed age.");

            // Real records are read on the baseline pass too, so seed one PAST every threshold: the
            // baseline must record the value without awarding the milestones it already passed.
            pawn.records.AddTo(RecordDefOf.Kills, 250f);

            scope.RequireNoNewEvent(() => Scan(Now()));

            PawnDiaryRimTestScope.Require(!state.baselineAnniversariesOnNextScan,
                "The baseline flag should be cleared after the first scan.");
            PawnDiaryRimTestScope.Require(
                state.lastObservedBiologicalAgeYears == pawn.ageTracker.AgeBiologicalYears,
                "The baseline scan did not record the pawn's current age.");
            PawnDiaryRimTestScope.Require(state.HighestRecordValue("Kills") >= 250f,
                "The baseline scan did not record the pawn's existing record value.");

            // A second scan at the same moment must still be silent: nothing changed.
            scope.RequireNoNewEvent(() => Scan(Now()));
        }

        /// <summary>
        /// A birthday emits exactly one quiet page carrying the exact age, and never repeats.
        /// </summary>
        [Test]
        public static void BirthdayEmitsOnceWithTheExactAge()
        {
            PawnProgressionState state = BaselinedState();
            int age = pawn.ageTracker.AgeBiologicalYears;
            // Pretend we last saw the pawn a year younger: the scanner then reads their real current
            // age as a birthday just reached, exactly as it would the tick after BirthdayBiological.
            state.lastObservedBiologicalAgeYears = age - 1;

            DiaryEvent birthday = scope.FireAndRequireEvent(
                () => Scan(Now()),
                ProgressionEventData.PawnBirthdayDefName,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);

            scope.RequireSoloRef(birthday, pawn);
            RequireContextField(birthday, AnniversaryPolicy.BirthdayAgeContextKey,
                age.ToString(System.Globalization.CultureInfo.InvariantCulture));
            RequireContextField(birthday, AnniversaryPolicy.OwnershipContextKey,
                AnniversaryPolicy.BirthdayOwnershipKey(pawn.GetUniqueLoadID(), age));
            RequireContextField(birthday, "progression_kind", AnniversaryPolicy.KindBirthday);
            PawnDiaryRimTestScope.Require(
                state.lastObservedBiologicalAgeYears == age,
                "The birthday page did not advance the observed age, so it could repeat.");

            // Same age, later scan: nothing new.
            scope.RequireNoNewEvent(() => Scan(Now()));

            // Even a damaged/replayed observation cursor cannot create a second page: the page above
            // is now the stable owner of this exact pawn/age.
            state.lastObservedBiologicalAgeYears = age - 1;
            scope.RequireNoNewEvent(() => Scan(Now()));
        }

        /// <summary>
        /// The no-duplicate rule: when a direct birthday page already owns this exact age — here
        /// RimWorld's own birthday event window — the anniversary scanner stays silent and simply
        /// advances past it. Matching is on stable schema tokens, never on translated prose.
        /// </summary>
        [Test]
        public static void BirthdayStaysSilentWhenADirectPageAlreadyOwnsTheAge()
        {
            PawnProgressionState state = BaselinedState();
            int age = pawn.ageTracker.AgeBiologicalYears;
            string pawnId = pawn.GetUniqueLoadID();

            // The shape RecordEventWindowBirthday produces: window key, subject id, and age-as-label.
            DiaryEvent owner = scope.Component.AddSoloEvent(
                pawn,
                null,
                AnniversaryPolicy.EventWindowBirthdayKey,
                "birthday",
                "fixture birthday window",
                string.Empty,
                "event_window=" + AnniversaryPolicy.EventWindowBirthdayKey
                    + "; phase=start; source=PawnAge; signal=birthday"
                    + "; def=" + AnniversaryPolicy.EventWindowBirthdayKey
                    + "; label=" + age.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "; subject=" + pawn.LabelShortCap
                    + "; subject_id=" + pawnId);
            PawnDiaryRimTestScope.Require(owner != null,
                "The fixture could not register the owning birthday page.");

            state.lastObservedBiologicalAgeYears = age - 1;
            scope.RequireNoNewEvent(() => Scan(Now()));
            scope.RequireNoEventForTestPawns(ProgressionEventData.PawnBirthdayDefName);
            PawnDiaryRimTestScope.Require(state.lastObservedBiologicalAgeYears == age,
                "A suppressed birthday must still advance the observed age, or it would retry forever.");
        }

        /// <summary>
        /// Ownership is per subject: a birthday page belonging to a DIFFERENT pawn must never silence
        /// this one, even when both pages name the same age. This is the other half of the suppression
        /// rule above, and it needs its own fresh pawn because only one age is ever in play per pawn.
        /// </summary>
        [Test]
        public static void BirthdayIgnoresAnotherPawnsBirthdayPage()
        {
            PawnProgressionState state = BaselinedState();
            int age = pawn.ageTracker.AgeBiologicalYears;

            DiaryEvent otherPawnsPage = scope.Component.AddSoloEvent(
                pawn,
                null,
                AnniversaryPolicy.EventWindowBirthdayKey,
                "birthday",
                "fixture birthday window naming another subject",
                string.Empty,
                "event_window=" + AnniversaryPolicy.EventWindowBirthdayKey
                    + "; label=" + age.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "; subject_id=PawnDiaryRimTest_SomeoneElse");
            PawnDiaryRimTestScope.Require(otherPawnsPage != null,
                "The fixture could not register the other pawn's birthday page.");

            state.lastObservedBiologicalAgeYears = age - 1;
            DiaryEvent birthday = scope.FireAndRequireEvent(
                () => Scan(Now()),
                ProgressionEventData.PawnBirthdayDefName,
                pawn,
                null);
            PawnDiaryRimTestScope.Require(birthday != null,
                "Another pawn's birthday page wrongly suppressed this pawn's birthday.");
        }

        /// <summary>
        /// Arrival anniversaries fire on the configured years and stay quiet in between, measured from
        /// the pawn's real arrival page. Every evaluated year advances the saved value, so a quiet year
        /// can never resurface later.
        /// </summary>
        [Test]
        public static void ArrivalAnniversaryEmitsOnMilestoneYearsOnly()
        {
            int arrivalTick = RegisterArrivalPage();
            PawnProgressionState state = BaselinedState(arrivalTick);
            PawnDiaryRimTestScope.Require(state.lastArrivalAnniversaryYear == 0,
                "A same-tick baseline should record zero elapsed arrival years.");

            RequireArrivalPageForYear(arrivalTick, state, 1);
            RequireArrivalPageForYear(arrivalTick, state, 2);
            RequireArrivalPageForYear(arrivalTick, state, 3);

            // Year 4 is deliberately quiet, but must still advance the saved year.
            scope.RequireNoNewEvent(() => Scan(YearsAfter(arrivalTick, 4)));
            PawnDiaryRimTestScope.Require(state.lastArrivalAnniversaryYear == 4,
                "A quiet arrival year must still advance, or it could resurface as a page later.");

            RequireArrivalPageForYear(arrivalTick, state, 5);

            scope.RequireNoNewEvent(() => Scan(YearsAfter(arrivalTick, 6)));
            PawnDiaryRimTestScope.Require(state.lastArrivalAnniversaryYear == 6,
                "Arrival year 6 must advance the saved year without emitting.");

            RequireArrivalPageForYear(arrivalTick, state, 10);
            // Above the highest configured milestone the recurring interval takes over.
            RequireArrivalPageForYear(arrivalTick, state, 15);
        }

        /// <summary>
        /// A remembered loss is certain for the guaranteed years, and the page carries the victim's
        /// name, relation, and elapsed years without leaking an internal id.
        /// </summary>
        [Test]
        public static void RememberedLossEmitsForGuaranteedYears()
        {
            PawnProgressionState state = BaselinedState();
            int deathTick = Now();
            SeedMemory(state, "PawnDiaryRimTest_Victim1", "Ada", "Spouse", "wife", deathTick);

            for (int year = 1; year <= DiaryTuning.Current.bondedDeathGuaranteedYears; year++)
            {
                int at = YearsAfter(deathTick, year);
                DiaryEvent page = scope.FireAndRequireEvent(
                    () => Scan(at),
                    ProgressionEventData.BondedDeathAnniversaryDefName,
                    pawn,
                    null);
                scope.RequireSoloRef(page, pawn);
                RequireContextField(page, AnniversaryPolicy.AnniversaryYearContextKey,
                    year.ToString(System.Globalization.CultureInfo.InvariantCulture));
                RequireContextField(page, AnniversaryPolicy.RememberedContextKey, "Ada (wife)");
                PawnDiaryRimTestScope.Require(
                    page.gameContext.IndexOf("PawnDiaryRimTest_Victim1", StringComparison.Ordinal) < 0
                        || page.gameContext.IndexOf(
                            AnniversaryPolicy.OwnershipContextKey, StringComparison.Ordinal) >= 0,
                    "The victim's internal id appeared outside the ownership key.");

                // Same year, second scan: the processed-year mark and the per-day guard both hold.
                scope.RequireNoNewEvent(() => Scan(at));
                // Let the next year's page through the once-per-day guard.
                state.lastBondedDeathPageDay = int.MinValue;
            }
        }

        /// <summary>
        /// Past the guaranteed years recall becomes a deterministic sample. Forcing the schedule to
        /// certainty and then to the floor proves both the emit and the silence paths, and that the
        /// evaluated year is marked either way so a reload cannot reroll it.
        /// </summary>
        [Test]
        public static void RememberedLossDecaysAfterTheGuaranteedYears()
        {
            PawnProgressionState state = BaselinedState();
            int deathTick = Now();
            SeedMemory(state, "PawnDiaryRimTest_Victim2", "Brik", "Lover", "lover", deathTick);

            DiaryTuningDef tuning = DiaryTuning.Current;
            float originalFirst = tuning.bondedDeathFirstDecayChance;
            float originalFloor = tuning.bondedDeathFloorChance;
            scope.RegisterCleanup(() =>
            {
                tuning.bondedDeathFirstDecayChance = originalFirst;
                tuning.bondedDeathFloorChance = originalFloor;
            });

            // Chance 0: year 4 is decided and skipped, but the year must still be marked processed.
            tuning.bondedDeathFirstDecayChance = 0f;
            tuning.bondedDeathFloorChance = 0f;
            scope.RequireNoNewEvent(() => Scan(YearsAfter(deathTick, 4)));
            PawnDiaryRimTestScope.Require(
                state.bondedDeathMemories[0].lastProcessedAnniversaryYear == 4,
                "A failed recall sample must still mark the year processed, or a reload rerolls it.");

            // Restoring certainty must NOT resurrect year 4 — the decision was already made.
            tuning.bondedDeathFirstDecayChance = 1f;
            tuning.bondedDeathFloorChance = 1f;
            scope.RequireNoNewEvent(() => Scan(YearsAfter(deathTick, 4)));

            // Year 5 is a fresh decision, and now certain.
            DiaryEvent page = scope.FireAndRequireEvent(
                () => Scan(YearsAfter(deathTick, 5)),
                ProgressionEventData.BondedDeathAnniversaryDefName,
                pawn,
                null);
            RequireContextField(page, AnniversaryPolicy.AnniversaryYearContextKey, "5");
        }

        /// <summary>
        /// Losses sharing one calendar date become ONE page naming at most the configured number of
        /// people, closest bond first — and every qualifying memory is marked processed, including the
        /// ones left off the page.
        /// </summary>
        [Test]
        public static void CoincidentLossesProduceOneCombinedPage()
        {
            PawnProgressionState state = BaselinedState();
            int deathTick = Now();
            deathTick = deathTick - (deathTick % GenDate.TicksPerDay) + 1000;
            SeedMemory(state, "PawnDiaryRimTest_V_a", "Ada", "Spouse", "wife", deathTick);
            SeedMemory(state, "PawnDiaryRimTest_V_b", "Brik", "Lover", "lover", deathTick + 1000);
            SeedMemory(state, "PawnDiaryRimTest_V_c", "Cass", "Child", "son", deathTick + 2000);
            SeedMemory(state, "PawnDiaryRimTest_V_d", "Dane", "Sibling", "brother", deathTick + 3000);

            DiaryEvent page = scope.FireAndRequireEvent(
                () => Scan(YearsAfter(deathTick, 1)),
                ProgressionEventData.BondedDeathAnniversaryDefName,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);

            // Comma-joined, because "; " is the context field separator: a semicolon here would make
            // the saved field parse back as just "Ada (wife)".
            RequireContextField(page, AnniversaryPolicy.RememberedContextKey,
                "Ada (wife), Brik (lover), Cass (son)");
            PawnDiaryRimTestScope.Require(
                page.gameContext.IndexOf("Dane", StringComparison.Ordinal) < 0,
                "The weakest coincident bond should have been left off the capped combined page.");

            for (int i = 0; i < state.bondedDeathMemories.Count; i++)
            {
                PawnDiaryRimTestScope.Require(
                    state.bondedDeathMemories[i].lastProcessedAnniversaryYear == 1,
                    "Every qualifying memory must be marked processed, including ones past the "
                    + "display cap, or the leftovers reappear on a later scan.");
            }

            // The locked rule: at most one combined bonded-death page per pawn per day.
            scope.RequireNoNewEvent(() => Scan(YearsAfter(deathTick, 1)));
        }

        /// <summary>
        /// A personal record emits on a real RecordDef crossing, reports only the highest newly passed
        /// threshold, and never repeats — not even after a modded reset rebuilds the same total.
        /// </summary>
        [Test]
        public static void RecordMilestoneEmitsOnHighWaterCrossingOnly()
        {
            PawnProgressionState state = BaselinedState();
            PawnDiaryRimTestScope.Require(
                DefDatabase<RecordDef>.GetNamedSilentFail("Kills") != null,
                "The base-game 'Kills' RecordDef is missing; the shipped H2 record rules cannot resolve.");

            // Cross straight past 10 and 25 in one go: only the highest passed threshold may emit.
            pawn.records.AddTo(RecordDefOf.Kills, 30f);
            DiaryEvent page = scope.FireAndRequireEvent(
                () => Scan(Now()),
                ProgressionEventData.RecordMilestoneDefName,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(page, pawn);
            RequireContextField(page, AnniversaryPolicy.RecordValueContextKey, "25");
            RequireContextField(page, AnniversaryPolicy.OwnershipContextKey,
                AnniversaryPolicy.RecordOwnershipKey(pawn.GetUniqueLoadID(), "Kills", 25));
            RequireContextField(page, "progression_kind", AnniversaryPolicy.KindRecord);
            PawnDiaryRimTestScope.Require(state.HighestRecordValue("Kills") >= 30f,
                "The record page did not raise the high-water mark.");

            // Same value: nothing new.
            scope.RequireNoNewEvent(() => Scan(Now()));

            // A modded reset lowers the live value. The high-water mark must not follow it down, and
            // rebuilding the same total must not re-award the milestone.
            state.SetRecordHighWater("Kills", 30f);
            float rebuilt = pawn.records.GetValue(RecordDefOf.Kills);
            PawnDiaryRimTestScope.Require(rebuilt >= 30f,
                "The fixture could not establish a record value above the crossed threshold.");
            scope.RequireNoNewEvent(() => Scan(Now()));
            PawnDiaryRimTestScope.Require(state.HighestRecordValue("Kills") >= 30f,
                "A record re-scan lowered the monotonic high-water mark.");

            // The NEXT threshold is still reachable.
            pawn.records.AddTo(RecordDefOf.Kills, 40f);
            DiaryEvent next = scope.FireAndRequireEvent(
                () => Scan(Now()),
                ProgressionEventData.RecordMilestoneDefName,
                pawn,
                null);
            RequireContextField(next, AnniversaryPolicy.RecordValueContextKey, "50");
        }

        /// <summary>Disabling a sub-feature's own group silences only that sub-feature.</summary>
        [Test]
        public static void DisablingOneGroupSilencesOnlyThatSubFeature()
        {
            PawnProgressionState state = BaselinedState();
            PawnDiaryMod.Settings.SetGroupEnabled(BirthdayGroupKey, false);

            state.lastObservedBiologicalAgeYears = pawn.ageTracker.AgeBiologicalYears - 1;
            pawn.records.AddTo(RecordDefOf.Kills, 12f);

            DiaryEvent record = scope.FireAndRequireEvent(
                () => Scan(Now()),
                ProgressionEventData.RecordMilestoneDefName,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);
            PawnDiaryRimTestScope.Require(record != null,
                "Disabling the birthday row also silenced the record row.");
            scope.RequireNoEventForTestPawns(ProgressionEventData.PawnBirthdayDefName);
            PawnDiaryRimTestScope.Require(
                state.lastObservedBiologicalAgeYears == pawn.ageTracker.AgeBiologicalYears,
                "A disabled birthday row must still advance observation, or re-enabling it would "
                + "invent a catch-up page for a birthday that already passed.");
        }

        // ----- helpers ------------------------------------------------------------------------------

        private static int Now()
        {
            return Find.TickManager.TicksGame;
        }

        private static int YearsAfter(int tick, int years)
        {
            return tick + (years * GenDate.TicksPerYear);
        }

        private static void Scan(int now)
        {
            scope.Component.ScanAnniversariesForPawn(
                pawn, now, DiaryGameComponent.SnapshotAnniversaryRecordDefs());
        }

        private static PawnProgressionState State()
        {
            PawnProgressionState state = scope.Component.AnniversaryStateFor(pawn);
            PawnDiaryRimTestScope.Require(state != null,
                "The fixture pawn has no saved progression state.");
            return state;
        }

        /// <summary>Runs the silent first scan and returns the now-baselined state.</summary>
        private static PawnProgressionState BaselinedState()
        {
            return BaselinedState(Now());
        }

        private static PawnProgressionState BaselinedState(int now)
        {
            scope.RequireNoNewEvent(() => Scan(now));
            PawnProgressionState state = State();
            PawnDiaryRimTestScope.Require(!state.baselineAnniversariesOnNextScan,
                "The baseline scan did not clear the H2 baseline flag.");
            return state;
        }

        /// <summary>
        /// Gives the pawn a real arrival page through the production ArrivalSignal, so the anniversary
        /// scanner measures from the same boundary the diary itself uses. Returns its tick.
        /// </summary>
        private static int RegisterArrivalPage()
        {
            DiaryEvent arrival = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new ArrivalSignal(pawn, "arrival_source=anniversary_fixture")),
                ArrivalSignal.ArrivalDefName,
                pawn,
                null);
            PawnDiaryRimTestScope.Require(arrival.IsArrivalDescriptionFor(pawn.GetUniqueLoadID()),
                "The fixture arrival page is not a neutral arrival description, so the anniversary "
                + "scanner would have no joining boundary to measure from.");
            return arrival.tick;
        }

        private static void RequireArrivalPageForYear(
            int arrivalTick,
            PawnProgressionState state,
            int year)
        {
            DiaryEvent page = scope.FireAndRequireEvent(
                () => Scan(YearsAfter(arrivalTick, year)),
                ProgressionEventData.ArrivalAnniversaryDefName,
                pawn,
                null);
            scope.RequireSoloRef(page, pawn);
            RequireContextField(page, AnniversaryPolicy.AnniversaryYearContextKey,
                year.ToString(System.Globalization.CultureInfo.InvariantCulture));
            RequireContextField(page, AnniversaryPolicy.OwnershipContextKey,
                AnniversaryPolicy.ArrivalOwnershipKey(pawn.GetUniqueLoadID(), year));
            PawnDiaryRimTestScope.Require(state.lastArrivalAnniversaryYear == year,
                "Arrival year " + year + " did not advance the saved year.");
        }

        /// <summary>
        /// Seeds one saved bonded-death memory. Live discovery needs a real colonist death, so the
        /// recall/aggregation tests start from the saved row the discovery pass would have produced.
        /// </summary>
        private static void SeedMemory(
            PawnProgressionState state,
            string victimId,
            string victimName,
            string relationDefName,
            string relationLabel,
            int deathTick)
        {
            if (state.bondedDeathMemories == null)
            {
                state.bondedDeathMemories = new List<BondedDeathMemoryState>();
            }

            state.bondedDeathMemories.Add(new BondedDeathMemoryState
            {
                victimId = victimId,
                victimName = victimName,
                relationDefName = relationDefName,
                relationLabel = relationLabel,
                deathTick = deathTick,
                lastProcessedAnniversaryYear = 0
            });
            // The discovery cursor would already be past this death in production; keeping it behind
            // the seeded tick would make discovery try to re-admit a victim it cannot resolve.
            state.lastBondedDeathDiscoveryTick = Math.Max(state.lastBondedDeathDiscoveryTick, deathTick);
        }

        private static void RequireContextField(DiaryEvent diaryEvent, string key, string expected)
        {
            PawnDiaryRimTestScope.Require(diaryEvent != null, "Expected a diary event to inspect.");
            string actual = DiaryContextFields.Value(diaryEvent.gameContext, key);
            PawnDiaryRimTestScope.Require(
                string.Equals(actual, expected, StringComparison.Ordinal),
                "Expected saved context '" + key + "=" + expected + "' but found '"
                    + key + "=" + actual + "' in: " + diaryEvent.gameContext);
        }
    }
}

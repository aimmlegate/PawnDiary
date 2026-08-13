// Anniversary and personal-record rules (Quality Wave §8, H2).
//
// Everything that decides WHETHER an anniversary or milestone is worth a page — and which year, which
// names, and in which order — is pure and lives in Source/Pipeline/AnniversaryPolicy.cs, so it is
// verified here without RimWorld: elapsed-year arithmetic, the arrival milestone set and its
// repeating tail, first-scan baseline silence, exact birthday ownership, bond retention ordering and
// eviction, the grief decay schedule and its floor, deterministic per-year sampling, coincident-loss
// aggregation, and monotonic record high-water crossings.
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        // GenDate lives in Verse, so the pure policy takes the year length as a parameter; the harness
        // supplies the real value through the shared TicksPerYear constant in OnThisDayDividerTests.

        private static void TestAnniversaryPolicy()
        {
            TestAnniversaryElapsedYears();
            TestArrivalMilestoneYears();
            TestBirthdayBaselineAndOwnership();
            TestBondRetentionOrdering();
            TestBondedDeathRecallSchedule();
            TestCoincidentLossAggregation();
            TestRecordHighWaterCrossings();
            TestAnniversaryOwnershipKeys();
            TestShippedAnniversaryTuningXml();
        }

        private static BondedDeathCandidate Bond(
            string victimId,
            int priority,
            int deathTick,
            string name = null,
            string relationLabel = null,
            int anniversaryYear = 0)
        {
            return new BondedDeathCandidate
            {
                victimId = victimId,
                victimName = name ?? victimId,
                relationDefName = "Spouse",
                relationLabel = relationLabel,
                bondPriority = priority,
                deathTick = deathTick,
                lastProcessedAnniversaryYear = anniversaryYear
            };
        }

        // --- Elapsed whole years ------------------------------------------------------------------
        private static void TestAnniversaryElapsedYears()
        {
            AssertEqual("H2 exactly one year elapsed",
                1, AnniversaryPolicy.YearsBetween(0, TicksPerYear, TicksPerYear));
            AssertEqual("H2 one tick short of a year is still zero",
                0, AnniversaryPolicy.YearsBetween(0, TicksPerYear - 1, TicksPerYear));
            AssertEqual("H2 whole years truncate, never round up",
                3, AnniversaryPolicy.YearsBetween(0, (TicksPerYear * 3) + TicksPerYear - 1, TicksPerYear));
            AssertEqual("H2 a future arrival tick cannot invent an anniversary",
                0, AnniversaryPolicy.YearsBetween(TicksPerYear, 0, TicksPerYear));
            AssertEqual("H2 a nonsensical year length yields no anniversary",
                0, AnniversaryPolicy.YearsBetween(0, TicksPerYear, 0));
            // Widened arithmetic: an adversarial saved tick pair must not overflow into a negative span.
            AssertEqual("H2 extreme tick span cannot overflow",
                0, AnniversaryPolicy.YearsBetween(int.MaxValue, int.MinValue, TicksPerYear));

            const int daysPerYear = 60;
            AssertEqual("H2 death anniversary is due on the matching calendar day",
                1, AnniversaryPolicy.AnniversaryYearOnCalendarDay(100, 160, daysPerYear));
            AssertEqual("H2 a later matching date reports the exact anniversary year",
                2, AnniversaryPolicy.AnniversaryYearOnCalendarDay(100, 220, daysPerYear));
            AssertEqual("H2 the day before a death anniversary is not due",
                0, AnniversaryPolicy.AnniversaryYearOnCalendarDay(100, 159, daysPerYear));
            AssertEqual("H2 the day after a death anniversary cannot consume it late",
                0, AnniversaryPolicy.AnniversaryYearOnCalendarDay(100, 161, daysPerYear));
            AssertEqual("H2 a much later matching date still reports its exact year",
                5, AnniversaryPolicy.AnniversaryYearOnCalendarDay(100, 400, daysPerYear));
            AssertEqual("H2 a nonsensical calendar year yields no anniversary",
                0, AnniversaryPolicy.AnniversaryYearOnCalendarDay(100, 160, 0));
        }

        // --- The arrival milestone set, and the repeating tail above it ----------------------------
        private static void TestArrivalMilestoneYears()
        {
            List<int> shipped = new List<int> { 1, 2, 3, 5, 10 };
            int[] expectedHits = { 1, 2, 3, 5, 10, 15, 20, 25, 100 };
            for (int i = 0; i < expectedHits.Length; i++)
            {
                AssertTrue("H2 arrival year " + expectedHits[i] + " is a milestone",
                    AnniversaryPolicy.IsArrivalMilestoneYear(expectedHits[i], shipped, 5));
            }

            int[] expectedMisses = { 0, 4, 6, 7, 8, 9, 11, 12, 13, 14, 16, 21 };
            for (int i = 0; i < expectedMisses.Length; i++)
            {
                AssertTrue("H2 arrival year " + expectedMisses[i] + " stays quiet",
                    !AnniversaryPolicy.IsArrivalMilestoneYear(expectedMisses[i], shipped, 5));
            }

            AssertTrue("H2 a negative arrival year is never a milestone",
                !AnniversaryPolicy.IsArrivalMilestoneYear(-3, shipped, 5));
            AssertTrue("H2 disabling the recurring interval stops the tail",
                !AnniversaryPolicy.IsArrivalMilestoneYear(15, shipped, 0));
            AssertTrue("H2 disabling the recurring interval keeps the listed years",
                AnniversaryPolicy.IsArrivalMilestoneYear(10, shipped, 0));
            AssertTrue("H2 an empty milestone list still honors the recurring interval",
                AnniversaryPolicy.IsArrivalMilestoneYear(4, new List<int>(), 4));
            AssertTrue("H2 a null milestone list does not throw and honors the interval",
                AnniversaryPolicy.IsArrivalMilestoneYear(6, null, 3));
            // A zero/negative configured milestone is junk, not a match for year 0.
            AssertTrue("H2 a junk zero milestone row is ignored",
                !AnniversaryPolicy.IsArrivalMilestoneYear(0, new List<int> { 0 }, 0));
        }

        // --- First-scan baseline silence, and exact birthday ownership -----------------------------
        private static void TestBirthdayBaselineAndOwnership()
        {
            int observed;
            AssertEqual("H2 a never-observed pawn baselines silently",
                0, AnniversaryPolicy.BirthdayToConsider(34, -1, false, out observed));
            AssertEqual("H2 the baseline pass still records the current age", 34, observed);

            AssertEqual("H2 an explicit baseline pass emits nothing",
                0, AnniversaryPolicy.BirthdayToConsider(35, 34, true, out observed));
            AssertEqual("H2 the baseline pass advances past the birthday anyway", 35, observed);

            AssertEqual("H2 an increased age is the birthday to consider",
                35, AnniversaryPolicy.BirthdayToConsider(35, 34, false, out observed));
            AssertEqual("H2 considering a birthday advances the observed age", 35, observed);

            AssertEqual("H2 an unchanged age is not a birthday",
                0, AnniversaryPolicy.BirthdayToConsider(35, 35, false, out observed));
            AssertEqual("H2 an unchanged age leaves the observed age alone", 35, observed);

            // Age can go DOWN in RimWorld (age reversal). That is not a birthday, and the high mark
            // must hold so the pawn does not "have" the same birthday twice on the way back up.
            AssertEqual("H2 a reversed age is not a birthday",
                0, AnniversaryPolicy.BirthdayToConsider(30, 35, false, out observed));
            AssertEqual("H2 a reversed age never lowers the observed high mark", 35, observed);

            AssertEqual("H2 an age of zero is never a birthday",
                0, AnniversaryPolicy.BirthdayToConsider(0, -1, false, out observed));

            // Ownership: RimWorld's own birthday event window claims the exact age for the exact pawn.
            AssertTrue("H2 the birthday event window owns its exact subject and age",
                AnniversaryPolicy.EventWindowPageOwnsBirthday(
                    "Birthday", "Thing_Human1", "35", "Thing_Human1", 35));
            AssertTrue("H2 another pawn's birthday page never suppresses this one",
                !AnniversaryPolicy.EventWindowPageOwnsBirthday(
                    "Birthday", "Thing_Human2", "35", "Thing_Human1", 35));
            AssertTrue("H2 a different age never suppresses this birthday",
                !AnniversaryPolicy.EventWindowPageOwnsBirthday(
                    "Birthday", "Thing_Human1", "34", "Thing_Human1", 35));
            AssertTrue("H2 an unrelated event window never suppresses a birthday",
                !AnniversaryPolicy.EventWindowPageOwnsBirthday(
                    "HeartAttack", "Thing_Human1", "35", "Thing_Human1", 35));
            AssertTrue("H2 a blank window key never suppresses a birthday",
                !AnniversaryPolicy.EventWindowPageOwnsBirthday(
                    null, "Thing_Human1", "35", "Thing_Human1", 35));
            AssertTrue("H2 a blank pawn id can never claim ownership",
                !AnniversaryPolicy.EventWindowPageOwnsBirthday(
                    "Birthday", "", "35", "", 35));
        }

        // --- Retention: closest bonds survive, and the order is total -----------------------------
        private static void TestBondRetentionOrdering()
        {
            List<BondedDeathCandidate> rows = new List<BondedDeathCandidate>
            {
                Bond("V_sibling", 6, 900),
                Bond("V_spouse", 0, 100),
                Bond("V_child", 3, 500)
            };
            List<BondedDeathCandidate> retained = AnniversaryPolicy.RetainStrongestBonds(rows, 3);
            AssertEqual("H2 retention keeps every row inside the cap", 3, retained.Count);
            AssertEqual("H2 the closest bond leads regardless of death order",
                "V_spouse", retained[0].victimId);
            AssertEqual("H2 the second-closest bond follows", "V_child", retained[1].victimId);
            AssertEqual("H2 the weakest bond trails", "V_sibling", retained[2].victimId);

            AssertEqual("H2 the cap evicts the weakest bond first",
                2, AnniversaryPolicy.RetainStrongestBonds(rows, 2).Count);
            AssertEqual("H2 eviction keeps the closest bond",
                "V_spouse", AnniversaryPolicy.RetainStrongestBonds(rows, 1)[0].victimId);

            // Equal bonds: the more recent loss wins, then the ordinal victim id.
            List<BondedDeathCandidate> equalBonds = new List<BondedDeathCandidate>
            {
                Bond("V_b", 2, 100),
                Bond("V_a", 2, 900),
                Bond("V_c", 2, 900)
            };
            List<BondedDeathCandidate> tieOrder = AnniversaryPolicy.RetainStrongestBonds(equalBonds, 3);
            AssertEqual("H2 equal bonds prefer the more recent loss", "V_a", tieOrder[0].victimId);
            AssertEqual("H2 an exact tie falls back to the ordinal victim id", "V_c", tieOrder[1].victimId);
            AssertEqual("H2 the oldest equal-bond loss trails", "V_b", tieOrder[2].victimId);

            // Rows the caller could not rank or identify are not memories at all.
            List<BondedDeathCandidate> unusable = new List<BondedDeathCandidate>
            {
                Bond("V_unranked", -1, 500),
                Bond(" ", 0, 500),
                null,
                Bond("V_ok", 4, 500)
            };
            List<BondedDeathCandidate> filtered = AnniversaryPolicy.RetainStrongestBonds(unusable, 16);
            AssertEqual("H2 unranked, blank, and null rows are dropped", 1, filtered.Count);
            AssertEqual("H2 the one usable row survives", "V_ok", filtered[0].victimId);

            AssertEqual("H2 a zero cap retains nothing",
                0, AnniversaryPolicy.RetainStrongestBonds(rows, 0).Count);
            AssertEqual("H2 a null input retains nothing",
                0, AnniversaryPolicy.RetainStrongestBonds(null, 16).Count);

            // Bond priority is the index in the XML strongest-first list; anything absent is refused.
            List<string> priority = new List<string> { "Spouse", "Lover", "Child", "Bond" };
            AssertEqual("H2 the first listed relation is the closest bond",
                0, AnniversaryPolicy.BondPriority("Spouse", priority));
            AssertEqual("H2 a later listed relation ranks lower",
                3, AnniversaryPolicy.BondPriority("Bond", priority));
            AssertEqual("H2 relation matching ignores case",
                2, AnniversaryPolicy.BondPriority("child", priority));
            AssertEqual("H2 an unlisted relation is never remembered",
                -1, AnniversaryPolicy.BondPriority("Cousin", priority));
            AssertEqual("H2 a blank relation is never remembered",
                -1, AnniversaryPolicy.BondPriority(" ", priority));
            AssertEqual("H2 a missing priority list remembers nothing",
                -1, AnniversaryPolicy.BondPriority("Spouse", null));
        }

        // --- Grief decays but never disappears ----------------------------------------------------
        private static void TestBondedDeathRecallSchedule()
        {
            AssertNear("H2 year 1 is guaranteed", 1f,
                AnniversaryPolicy.RecallChance(1, 3, 0.60f, 0.65f, 0.05f));
            AssertNear("H2 year 3 is still guaranteed", 1f,
                AnniversaryPolicy.RecallChance(3, 3, 0.60f, 0.65f, 0.05f));
            AssertNear("H2 year 4 is the first decayed year", 0.60f,
                AnniversaryPolicy.RecallChance(4, 3, 0.60f, 0.65f, 0.05f));
            AssertNear("H2 year 5 multiplies once", 0.39f,
                AnniversaryPolicy.RecallChance(5, 3, 0.60f, 0.65f, 0.05f));
            AssertNear("H2 year 6 multiplies twice", 0.2535f,
                AnniversaryPolicy.RecallChance(6, 3, 0.60f, 0.65f, 0.05f));
            AssertNear("H2 a distant year settles on the floor, never zero", 0.05f,
                AnniversaryPolicy.RecallChance(40, 3, 0.60f, 0.65f, 0.05f));
            AssertNear("H2 year zero is not an anniversary", 0f,
                AnniversaryPolicy.RecallChance(0, 3, 0.60f, 0.65f, 0.05f));
            // A hostile config (no decay at all) must terminate rather than loop on a far-future year.
            AssertNear("H2 a non-decaying multiplier still terminates", 0.60f,
                AnniversaryPolicy.RecallChance(1000, 3, 0.60f, 1f, 0.05f));

            AssertTrue("H2 a guaranteed year recalls at any roll",
                AnniversaryPolicy.ShouldRecall(0.999f, 1f));
            AssertTrue("H2 a roll below the chance recalls",
                AnniversaryPolicy.ShouldRecall(0.59f, 0.60f));
            AssertTrue("H2 the comparison is half-open, so the boundary does not recall",
                !AnniversaryPolicy.ShouldRecall(0.60f, 0.60f));
            AssertTrue("H2 a zero chance never recalls",
                !AnniversaryPolicy.ShouldRecall(0f, 0f));
            AssertTrue("H2 an out-of-range roll never recalls",
                !AnniversaryPolicy.ShouldRecall(1f, 1f));

            float effectiveChance;
            AssertTrue("H2 native recall chance combines with a reduced group multiplier",
                DiaryFrequencyPolicy.TryCalculateEffectiveChance(0.60f, 0.50f, out effectiveChance));
            AssertNear("H2 reduced frequency is folded into the upstream recall chance",
                0.30f, effectiveChance);
            AssertTrue("H2 the combined chance keeps the strict half-open acceptance boundary",
                AnniversaryPolicy.ShouldRecall(0.299f, effectiveChance));
            AssertTrue("H2 equality with the combined chance is still rejected",
                !AnniversaryPolicy.ShouldRecall(0.30f, effectiveChance));
            AssertTrue("H2 an increased group multiplier remains bounded",
                DiaryFrequencyPolicy.TryCalculateEffectiveChance(0.60f, 2f, out effectiveChance));
            AssertNear("H2 an increased group multiplier clamps the combined chance",
                1f, effectiveChance);

            // Deterministic sampling: the same triple always answers the same way, and every part of
            // the triple changes the answer.
            float roll = AnniversaryPolicy.DeterministicRoll("Thing_Human1", "Thing_Human2", 5);
            AssertNear("H2 the same pawn, victim, and year always roll the same",
                roll, AnniversaryPolicy.DeterministicRoll("Thing_Human1", "Thing_Human2", 5));
            AssertTrue("H2 the roll is inside [0, 1)", roll >= 0f && roll < 1f);
            AssertTrue("H2 a different year samples independently",
                AnniversaryPolicy.DeterministicRoll("Thing_Human1", "Thing_Human2", 6) != roll);
            AssertTrue("H2 a different victim samples independently",
                AnniversaryPolicy.DeterministicRoll("Thing_Human1", "Thing_Human3", 5) != roll);
            AssertTrue("H2 a different pawn samples independently",
                AnniversaryPolicy.DeterministicRoll("Thing_Human9", "Thing_Human2", 5) != roll);
            // Length-prefixing keeps component boundaries unambiguous.
            AssertTrue("H2 concatenation-ambiguous ids sample independently",
                AnniversaryPolicy.DeterministicRoll("ab", "c", 1)
                    != AnniversaryPolicy.DeterministicRoll("a", "bc", 1));
        }

        // --- Several losses on one date become one page -------------------------------------------
        private static void TestCoincidentLossAggregation()
        {
            List<BondedDeathCandidate> due = new List<BondedDeathCandidate>
            {
                Bond("V_d", 6, 400, "Dane", "brother", 9),
                Bond("V_a", 0, 100, "Ada", "wife", 3),
                Bond("V_b", 2, 200, "Brik", "lover", 5),
                Bond("V_c", 3, 300, "Cass", "son", 7)
            };
            List<BondedDeathCandidate> selected = AnniversaryPolicy.SelectCoincident(due, 3);
            AssertEqual("H2 a combined page is capped at the configured names", 3, selected.Count);
            AssertEqual("H2 the closest bond leads the combined page", "V_a", selected[0].victimId);
            AssertEqual("H2 the weakest due bond is left off the page", "V_c", selected[2].victimId);

            AssertEqual("H2 combined remembrance names people and relations",
                "Ada (wife), Brik (lover), Cass (son)",
                AnniversaryPolicy.FormatRemembered(selected));
            AssertEqual("H2 a single remembrance still reads as a person",
                "Ada (wife)",
                AnniversaryPolicy.FormatRemembered(new List<BondedDeathCandidate> { selected[0] }));
            AssertEqual("H2 a nameless memory is omitted rather than rendered blank",
                string.Empty,
                AnniversaryPolicy.FormatRemembered(
                    new List<BondedDeathCandidate> { Bond("V_x", 0, 1, " ", "wife") }));
            AssertEqual("H2 a missing relation label still names the person",
                "Ada",
                AnniversaryPolicy.FormatRemembered(
                    new List<BondedDeathCandidate> { Bond("V_a", 0, 1, "Ada", null) }));
            AssertEqual("H2 empty remembrance omits the whole prompt field",
                string.Empty, AnniversaryPolicy.FormatRemembered(null));

            // A name carrying the context grammar must not be able to split the "; key=value" string,
            // and neither may our own list separator. The combined form is the case that regressed in
            // the first in-game run: joining names with "; " made the saved label= and remembered=
            // fields parse back as just the first name, because that is the field separator itself.
            string hostile = AnniversaryPolicy.FormatRemembered(
                new List<BondedDeathCandidate> { Bond("V_h", 0, 1, "Ada; anniversary_year=99", "wife") });
            AssertTrue("H2 a hostile name cannot inject a context field",
                hostile.IndexOf("; anniversary_year=", System.StringComparison.Ordinal) < 0);
            AssertTrue("H2 no remembrance line carries the context field separator",
                AnniversaryPolicy.FormatRemembered(selected).IndexOf(';') < 0 && hostile.IndexOf(';') < 0);
            AssertTrue("H2 no remembrance line carries a context key assignment",
                AnniversaryPolicy.FormatRemembered(selected).IndexOf('=') < 0 && hostile.IndexOf('=') < 0);
            AssertEqual("H2 the year the page remembers travels with each name",
                3, selected[0].lastProcessedAnniversaryYear);
        }

        // --- Records only ever go up --------------------------------------------------------------
        private static void TestRecordHighWaterCrossings()
        {
            List<int> thresholds = new List<int> { 10, 25, 50, 100 };

            RecordMilestoneCrossing baseline = AnniversaryPolicy.EvaluateRecord(
                "Kills", 60f, 0f, thresholds, true);
            AssertTrue("H2 the baseline pass emits no record page", !baseline.shouldEmit);
            AssertNear("H2 the baseline pass records where the pawn already is",
                60f, baseline.newHighWater);

            RecordMilestoneCrossing first = AnniversaryPolicy.EvaluateRecord(
                "Kills", 12f, 0f, thresholds, false);
            AssertTrue("H2 a newly crossed threshold emits", first.shouldEmit);
            AssertEqual("H2 the crossed threshold is the one reported", 10, first.threshold);

            RecordMilestoneCrossing jumped = AnniversaryPolicy.EvaluateRecord(
                "Kills", 60f, 12f, thresholds, false);
            AssertTrue("H2 a jump across several thresholds still emits", jumped.shouldEmit);
            AssertEqual("H2 only the highest newly crossed threshold emits", 50, jumped.threshold);
            AssertNear("H2 the high-water mark follows the live value", 60f, jumped.newHighWater);

            RecordMilestoneCrossing repeat = AnniversaryPolicy.EvaluateRecord(
                "Kills", 60f, 60f, thresholds, false);
            AssertTrue("H2 an already-awarded threshold never repeats", !repeat.shouldEmit);

            // A modded record reset lowers the live value; the high-water mark must not follow it down,
            // and rebuilding the same total must not award the milestone a second time.
            RecordMilestoneCrossing afterReset = AnniversaryPolicy.EvaluateRecord(
                "Kills", 0f, 60f, thresholds, false);
            AssertTrue("H2 a record reset emits nothing", !afterReset.shouldEmit);
            AssertNear("H2 a record reset never lowers the high-water mark",
                60f, afterReset.newHighWater);
            AssertTrue("H2 rebuilding to the same total does not re-award the milestone",
                !AnniversaryPolicy.EvaluateRecord("Kills", 55f, 60f, thresholds, false).shouldEmit);
            AssertTrue("H2 passing the NEXT threshold after a reset still emits",
                AnniversaryPolicy.EvaluateRecord("Kills", 101f, 60f, thresholds, false).shouldEmit);

            AssertTrue("H2 a value below every threshold emits nothing",
                !AnniversaryPolicy.EvaluateRecord("Kills", 9f, 0f, thresholds, false).shouldEmit);
            AssertTrue("H2 exactly reaching a threshold counts as crossing it",
                AnniversaryPolicy.EvaluateRecord("Kills", 10f, 0f, thresholds, false).shouldEmit);
            AssertTrue("H2 a record with no thresholds emits nothing",
                !AnniversaryPolicy.EvaluateRecord("Kills", 500f, 0f, null, false).shouldEmit);
            AssertTrue("H2 a blank record name emits nothing",
                !AnniversaryPolicy.EvaluateRecord(" ", 500f, 0f, thresholds, false).shouldEmit);

            AssertNear("H2 a NaN live value is treated as no information",
                60f, AnniversaryPolicy.HighestValue(60f, float.NaN));
            AssertNear("H2 a negative live value is treated as zero",
                0f, AnniversaryPolicy.HighestValue(0f, -5f));
        }

        // --- Exact ownership keys ----------------------------------------------------------------
        private static void TestAnniversaryOwnershipKeys()
        {
            AssertEqual("H2 birthday ownership key",
                "birthday|Thing_Human1|35",
                AnniversaryPolicy.BirthdayOwnershipKey("Thing_Human1", 35));
            AssertEqual("H2 arrival ownership key",
                "arrival_anniversary|Thing_Human1|10",
                AnniversaryPolicy.ArrivalOwnershipKey("Thing_Human1", 10));
            AssertEqual("H2 record ownership key",
                "record|Thing_Human1|Kills|50",
                AnniversaryPolicy.RecordOwnershipKey("Thing_Human1", "Kills", 50));

            // The combined death key must not depend on discovery order.
            string ordered = AnniversaryPolicy.DeathOwnershipKey(
                "Thing_Human1", 3, new List<string> { "V_a", "V_b" });
            AssertEqual("H2 combined death ownership key",
                "death_anniversary|Thing_Human1|3|V_a,V_b", ordered);
            AssertEqual("H2 the combined death key ignores discovery order",
                ordered,
                AnniversaryPolicy.DeathOwnershipKey(
                    "Thing_Human1", 3, new List<string> { "V_b", "V_a" }));
            AssertEqual("H2 the combined death key de-duplicates victims",
                ordered,
                AnniversaryPolicy.DeathOwnershipKey(
                    "Thing_Human1", 3, new List<string> { "V_b", "V_a", "V_b" }));
            AssertEqual("H2 a death key with no victims cannot claim ownership",
                string.Empty,
                AnniversaryPolicy.DeathOwnershipKey("Thing_Human1", 3, new List<string>()));

            // Fail closed: a key part that would corrupt the context grammar yields no key at all,
            // which callers treat as "cannot claim ownership" and skip.
            AssertEqual("H2 a blank pawn id yields no ownership key",
                string.Empty, AnniversaryPolicy.BirthdayOwnershipKey(" ", 35));
            AssertEqual("H2 a pawn id carrying a separator yields no ownership key",
                string.Empty, AnniversaryPolicy.BirthdayOwnershipKey("Thing|Human1", 35));
            AssertEqual("H2 a pawn id carrying the context grammar yields no ownership key",
                string.Empty, AnniversaryPolicy.BirthdayOwnershipKey("Thing;Human1", 35));
            AssertEqual("H2 a record name carrying a separator yields no ownership key",
                string.Empty, AnniversaryPolicy.RecordOwnershipKey("Thing_Human1", "Kills=1", 50));
            AssertEqual("H2 a victim id carrying a separator is excluded from the key",
                string.Empty,
                AnniversaryPolicy.DeathOwnershipKey("Thing_Human1", 3, new List<string> { "V,a" }));
            AssertEqual("H2 display fields cannot inject context separators",
                "meals, cooked - master",
                AnniversaryPolicy.ContextFieldText(" meals; cooked = master "));
        }

        // --- The shipped XML must still match the values this feature was tuned against ----------
        private static void TestShippedAnniversaryTuningXml()
        {
            XDocument tuning = XDocument.Load(RepoPath("1.6", "Defs", "DiaryTuningDef.xml"));
            AssertEqual("H2 shipped scan interval", "15000",
                tuning.Descendants("anniversaryScanIntervalTicks").Single().Value.Trim());
            AssertEqual("H2 shipped arrival milestone years", "1,2,3,5,10",
                string.Join(",", tuning.Descendants("arrivalAnniversaryMilestoneYears")
                    .Single().Elements("li").Select(row => row.Value.Trim()).ToArray()));
            AssertEqual("H2 shipped recurring arrival interval", "5",
                tuning.Descendants("arrivalAnniversaryRecurringIntervalYears").Single().Value.Trim());
            AssertEqual("H2 shipped bond priority is closest-first",
                "Spouse,Fiance,Lover,Child,Parent,Bond,Sibling",
                string.Join(",", tuning.Descendants("bondedDeathRelationPriority")
                    .Single().Elements("li").Select(row => row.Value.Trim()).ToArray()));
            AssertEqual("H2 shipped memory cap", "16",
                tuning.Descendants("bondedDeathMemoryCap").Single().Value.Trim());
            AssertEqual("H2 shipped guaranteed grief years", "3",
                tuning.Descendants("bondedDeathGuaranteedYears").Single().Value.Trim());
            AssertEqual("H2 shipped first decayed chance", "0.60",
                tuning.Descendants("bondedDeathFirstDecayChance").Single().Value.Trim());
            AssertEqual("H2 shipped decay multiplier", "0.65",
                tuning.Descendants("bondedDeathDecayMultiplier").Single().Value.Trim());
            AssertEqual("H2 shipped recall floor", "0.05",
                tuning.Descendants("bondedDeathFloorChance").Single().Value.Trim());
            AssertEqual("H2 shipped combined-name cap", "3",
                tuning.Descendants("bondedDeathMaxCombinedNames").Single().Value.Trim());

            // Record defNames must stay base-game so a no-DLC install still sees all three.
            XElement records = tuning.Descendants("recordMilestones").Single();
            string[] expected =
            {
                "Kills|10,25,50,100",
                "MealsCooked|100,500,1000,5000",
                "ThingsConstructed|100,500,1000,5000"
            };
            string[] actual = records.Elements("li")
                .Select(row => ChildValue(row, "recordDefName") + "|"
                    + string.Join(",", row.Element("thresholds").Elements("li")
                        .Select(value => value.Value.Trim()).ToArray()))
                .ToArray();
            AssertEqual("H2 shipped record rule count", expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                AssertEqual("H2 shipped record rule " + i, expected[i], actual[i]);
            }

            // Four independently toggleable Progression rows; only the remembered loss is important,
            // so a birthday or a tally can never outrank a real event inside a reflection.
            XDocument groups = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryInteractionGroupDefs.xml"));
            string[] groupDefNames =
            {
                "progressionBirthday",
                "progressionArrivalAnniversary",
                "progressionDeathAnniversary",
                "progressionRecordMilestone"
            };
            string[] sourceDefNames =
            {
                "PawnBirthday",
                "ArrivalAnniversary",
                "BondedDeathAnniversary",
                "RecordMilestone"
            };
            string[] expectedImportance = { "false", "true", "false" };
            for (int i = 0; i < groupDefNames.Length; i++)
            {
                XElement group = groups.Descendants("PawnDiary.DiaryInteractionGroupDef")
                    .Single(row => ChildValue(row, "defName") == groupDefNames[i]);
                AssertEqual("H2 " + groupDefNames[i] + " stays in the Progression domain",
                    "Progression", ChildValue(group, "domain"));
                AssertEqual("H2 " + groupDefNames[i] + " ships enabled",
                    "true", ChildValue(group, "defaultEnabled"));
                AssertEqual("H2 " + groupDefNames[i] + " matches its exact source name",
                    sourceDefNames[i],
                    group.Element("matchDefNames").Elements("li").Single().Value.Trim());
                AssertTrue("H2 " + groupDefNames[i] + " is never a catch-all",
                    group.Element("catchAll") == null);
                AssertEqual("H2 " + groupDefNames[i] + " importance",
                    i == 2 ? expectedImportance[1] : expectedImportance[0],
                    ChildValue(group, "important"));
                AssertEqual("H2 " + groupDefNames[i] + " follows the planned white cue",
                    "white", ChildValue(group, "colorCue"));
            }

            // One dedicated prompt row per source, keyed to the exact source name so every other
            // progression page keeps the broad DiaryEventPrompt_Progression policy.
            XDocument prompts = XDocument.Load(RepoPath("1.6", "Defs", "DiaryEventPromptDefs.xml"));
            for (int i = 0; i < sourceDefNames.Length; i++)
            {
                XElement prompt = prompts.Descendants("PawnDiary.DiaryEventPromptDef")
                    .Single(row => ChildValue(row, "eventType") == sourceDefNames[i]);
                AssertTrue("H2 " + sourceDefNames[i] + " prompt row supplies a prompt",
                    !string.IsNullOrWhiteSpace(ChildValue(prompt, "prompt")));
                AssertTrue("H2 " + sourceDefNames[i] + " prompt row supplies an enhancement",
                    !string.IsNullOrWhiteSpace(ChildValue(prompt, "enhancement")));
            }

            // EN + RU parity for every new Keyed string.
            string[] keys =
            {
                "PawnDiary.Event.AnniversaryBirthdayLabel",
                "PawnDiary.Event.AnniversaryBirthdayText",
                "PawnDiary.Event.AnniversaryArrivalLabel",
                "PawnDiary.Event.AnniversaryArrivalText",
                "PawnDiary.Event.AnniversaryDeathLabel",
                "PawnDiary.Event.AnniversaryDeathText",
                "PawnDiary.Event.AnniversaryDeathCombinedText",
                "PawnDiary.Event.AnniversaryDeathCombinedEntry",
                "PawnDiary.Event.AnniversaryRecordLabel",
                "PawnDiary.Event.AnniversaryRecordText"
            };
            XDocument english = XDocument.Load(
                RepoPath("Languages", "English", "Keyed", "PawnDiary.xml"));
            XDocument russian = XDocument.Load(
                RepoPath("Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            for (int i = 0; i < keys.Length; i++)
            {
                AssertTrue("H2 English keyed string " + keys[i],
                    english.Descendants(keys[i]).Count() == 1);
                AssertTrue("H2 Russian keyed string " + keys[i],
                    russian.Descendants(keys[i]).Count() == 1);
            }
        }
    }
}

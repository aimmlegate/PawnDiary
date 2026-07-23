// In-game health-condition capture tests for Pawn Diary's hediff signal path (design/TEST_COVERAGE_PLAN.md
// §3, EVT-11). The AddHediff Harmony hook (HealthTrackerAddHediffPatch) forwards colonist hediffs to
// HediffSignal, which classifies each against the XML Hediff-domain groups and either writes an
// immediate solo diary page or defers the change to the end-of-day reflection (a "day-signal"). These
// tests drive the real vanilla `Pawn_HealthTracker.AddHediff` choke point and assert the routing:
//   - EVT-11a: an added body part (peg leg) matches the Immediate "artificial body parts" group and
//     produces one solo diary event carrying the added-part + affected-body-part markers.
//   - EVT-11b: a plain injury (bruise) is ignored by every hediff group (excludeInjuries) and produces
//     nothing — injuries are owned by the death/tale pages, not the hediff page.
//   - EVT-11c: a worsening major-health condition (flu) is a day-signal: neither its appearance nor a
//     severity-step change writes an immediate page; both defer to the deferred day reflection.
//
// All the fragile scaffolding — isolated non-generating pawns, snapshots, failure-safe teardown, and
// the no-leak audit — lives in the shared PawnDiaryRimTestScope harness. Every added hediff is removed
// through RegisterCleanup so the harness audit passes.
using System;
using RimWorld;
using RimTestRedux;
using Verse;
using PawnDiary.Ingestion;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that health-condition changes reach the right Pawn Diary page: added parts create an
    /// immediate solo event with body-part markers, ignored hediffs (injuries) are dropped, and
    /// severity-tracked major conditions route to the deferred day-signal instead of an immediate page.
    /// Requires a loaded game because the production capture pipeline ignores events at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryHediffFlowTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        /// <summary>
        /// Opens a fresh scope, enables the two hediff groups this suite drives (the Immediate
        /// artificial-body-part group and the DayReflection major-health catch-all), and creates one
        /// isolated adult colonist with generation disabled.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("hediffPartGainedArtificial", "hediffMajorHealth");
            pawn = scope.CreateAdultColonist();
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, or log row survived —
        /// even when the test above threw partway through.
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
        /// EVT-11a. Installs a peg leg (a Hediff_AddedPart) on a leg and verifies the AddHediff hook
        /// records one Immediate solo diary event whose interaction Def is the hediff's defName and
        /// whose gameContext carries the added-part body marker and the affected body-part label.
        /// </summary>
        [Test]
        public static void AddedBodyPartCreatesImmediateSoloEvent()
        {
            HediffDef pegLeg = RequireDef<HediffDef>("PegLeg");
            BodyPartRecord leg = RequireBodyPart(pawn, "Leg");

            Hediff hediff = HediffMaker.MakeHediff(pegLeg, pawn);
            scope.RegisterCleanup(() => RemoveHediffIfPresent(pawn, hediff));

            // The stored interactionDefName for a hediff event is the hediff's own defName (AddSoloEvent
            // is called with data.DefName = HediffDefName(hediff)), NOT any vanilla interaction name.
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => pawn.health.AddHediff(hediff, leg),
                "PegLeg",
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("part_kind=addedpart", StringComparison.OrdinalIgnoreCase) >= 0,
                "The hediff event context did not carry the added-part body marker.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("body_part=", StringComparison.OrdinalIgnoreCase) >= 0,
                "The hediff event context did not carry the affected body-part label.");
        }

        /// <summary>
        /// EVT-11b. Adds a plain bruise (a Hediff_Injury). Every hediff group sets excludeInjuries, so
        /// the signal fails the basic policy gate and no diary event is created — the negative gate.
        /// </summary>
        [Test]
        public static void InjuryHediffProducesNoEvent()
        {
            HediffDef bruise = RequireDef<HediffDef>("Bruise");
            BodyPartRecord torso = RequireBodyPart(pawn, "Torso");

            Hediff hediff = HediffMaker.MakeHediff(bruise, pawn);
            hediff.Severity = 5f;
            scope.RegisterCleanup(() => RemoveHediffIfPresent(pawn, hediff));

            scope.RequireNoNewEvent(() => pawn.health.AddHediff(hediff, torso));
        }

        /// <summary>
        /// EVT-11c. A worsening major-health condition (flu) is a day-signal: neither its appearance nor
        /// a severity-step change writes an immediate diary page. Both defer to the end-of-day
        /// reflection (the catch-all major-health group's DayReflection mode). The severity step is
        /// driven by submitting exactly the Progressed HediffSignal the periodic severity scan submits
        /// on a real worsen, because that scan (ScanHediffProgressionsForDiaryEvents) is private and has
        /// no internal test seam.
        /// </summary>
        [Test]
        public static void MajorHealthSeverityStepRoutesToDaySignal()
        {
            HediffDef flu = RequireDef<HediffDef>("Flu");

            Hediff hediff = HediffMaker.MakeHediff(flu, pawn);
            hediff.Severity = 0.4f;
            scope.RegisterCleanup(() => RemoveHediffIfPresent(pawn, hediff));

            // Adding the condition is a day-signal, not an immediate diary page.
            scope.RequireNoNewEvent(() => pawn.health.AddHediff(hediff));

            // The condition classifies to the catch-all major-health group, whose documented route is
            // the deferred end-of-day reflection (DayReflection), with severity tracking enabled.
            DiaryInteractionGroupDef group;
            HediffSignalPolicy policy;
            bool matched = DiaryGameComponent.TryGetHediffPolicy(hediff, out group, out policy);
            PawnDiaryRimTestScope.Require(
                matched && group != null && policy != null,
                "The flu condition did not resolve to a Hediff-domain diary policy.");
            PawnDiaryRimTestScope.Require(
                string.Equals(group.defName, "hediffMajorHealth", StringComparison.Ordinal),
                "The flu condition did not classify to the major-health catch-all group.");
            PawnDiaryRimTestScope.Require(
                policy.mode == HediffDiaryMode.DayReflection,
                "The major-health group is expected to route health changes to the day reflection.");
            PawnDiaryRimTestScope.Require(
                policy.recordOnSeverityIncrease,
                "The major-health group is expected to track severity increases.");

            // A severity step change also defers to the day summary — never an immediate page.
            hediff.Severity = 0.7f;
            scope.RequireNoNewEvent(
                () => DiaryEvents.Submit(new HediffSignal(pawn, hediff, HediffSignalSource.Progressed)));
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

        private static BodyPartRecord RequireBodyPart(Pawn pawn, string bodyPartDefName)
        {
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (part?.def != null
                    && string.Equals(part.def.defName, bodyPartDefName, StringComparison.Ordinal))
                {
                    return part;
                }
            }

            throw new AssertionException(
                "Test pawn is missing a '" + bodyPartDefName + "' body part for the hediff test.");
        }

        private static void RemoveHediffIfPresent(Pawn pawn, Hediff hediff)
        {
            if (pawn?.health?.hediffSet?.hediffs != null
                && hediff != null
                && pawn.health.hediffSet.hediffs.Contains(hediff))
            {
                pawn.health.RemoveHediff(hediff);
            }
        }
    }
}

// In-game death-capture tests for Pawn Diary's Pawn.Kill Harmony hook (EVT-10, TEST_COVERAGE_PLAN.md §3).
//
// A colonist death has no vanilla death Tale when it carries no DamageInfo (the condition/need death
// path), so `pawn.Kill(null)` flows through PawnKillPatch.Postfix → DeathFallbackSignal, which records a
// single NEUTRAL death-description page (third-person "how they died", never a first-person diary entry
// in the dead pawn's voice) and files it on the pawn's diary via AddDeathEventRef. This suite proves:
//   (a) one neutral death page exists for the killed colonist afterward, found by the death-description
//       predicate (DiaryEvent.HasDeathDescription/IsDeathDescriptionFor and the component's
//       HasDeathDescriptionFor) — not by the solo-first-person shape;
//   (b) that page is neutral, asserted through the death-description API rather than RequireSoloRef;
//   (c) cross-source dedup: once a death page exists, a further death source for the same pawn (here a
//       redundant DeathFallbackSignal) is dropped by the shared DeathDescriptionKey / HasExistingDeath
//       guard — the Tale death route and the Pawn.Kill fallback can never both write a page.
//
// All the fragile scaffolding — isolated non-generating pawns, snapshots, RNG isolation, and
// failure-safe teardown — lives in the shared PawnDiaryRimTestScope harness. The one thing the harness
// does not own is the state a *killed* pawn leaves behind (a Corpse holder, world-pawn registry
// membership); this suite registers that cleanup before the destructive Kill so a failing assertion can
// never strand it. These tests never enable per-pawn generation, so no LLM request can leave the game
// (the neutral death description queues generation only through the pawn's disabled diary record).
using System;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that a real <see cref="Verse.Pawn.Kill(DamageInfo?, Hediff)"/> of a colonist reaches Pawn
    /// Diary's persisted event store as exactly one neutral death-description page, that the page is
    /// neutral (not a first-person solo entry), and that a second death source for the same pawn is
    /// deduped. These tests require a loaded game because the capture pipeline ignores events at the main
    /// menu, and a loaded map because vanilla death handling references the colony.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryDeathFlowTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn victim;

        /// <summary>
        /// Opens a fresh scope, enables the Tale-domain group that classifies the neutral death fallback
        /// (talecombat — its matchDefNames include PawnDiary_DeathFallback), and creates one isolated,
        /// generation-disabled adult colonist to kill.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("talecombat");

            // Vanilla death handling (mood, tales, notifications) reads the colony's home map, so gate on
            // a loaded map. The harness's failure-safe TearDown still runs and pops the RNG state.
            if (Find.CurrentMap == null)
            {
                throw new AssertionException("EVT-10 death tests require a loaded map with a colony.");
            }

            victim = scope.CreateAdultColonist();
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, or corpse survived — even
        /// when the test above threw partway through.
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
                victim = null;
            }
        }

        /// <summary>
        /// EVT-10. Killing a colonist with no DamageInfo records exactly one neutral death-description
        /// page for that pawn: it is found by the death-description predicate, it is neutral (third-person
        /// death description, not a first-person solo entry — asserted via the death-description API, not
        /// RequireSoloRef), and it is filed as the pawn's final-boundary diary reference.
        /// </summary>
        [Test]
        public static void KillingColonistRecordsOneNeutralDeathPage()
        {
            string victimId = victim.GetUniqueLoadID();

            // Register the dead-pawn cleanup BEFORE the destructive Kill so a failing assertion below can
            // never strand a corpse or world-pawn entry in the developer's colony.
            RegisterDeadPawnCleanup(victim);

            // Kill(null) carries no DamageInfo, so vanilla records no death Tale and the page comes from
            // the Pawn.Kill fallback (interactionDefName == PawnDiary_DeathFallback). FireAndRequireEvent
            // asserts exactly one such new event for the victim and returns it.
            DiaryEvent deathEvent = scope.FireAndRequireEvent(
                () => victim.Kill(null),
                DeathFallbackSignal.DeathFallbackDefName,
                victim,
                null);

            // (b) Neutral death description — assert through the death-description API, NOT RequireSoloRef
            // (RequireSoloRef would frame it as a first-person solo diary entry, which a death page is not).
            PawnDiaryRimTestScope.Require(
                deathEvent.HasDeathDescription(),
                "The death event did not carry a neutral death description.");
            PawnDiaryRimTestScope.Require(
                deathEvent.IsDeathDescriptionFor(victimId),
                "The death event was not a neutral death description for the killed colonist.");
            PawnDiaryRimTestScope.Require(
                deathEvent.gameContext != null
                    && deathEvent.gameContext.IndexOf("death_description=true", StringComparison.OrdinalIgnoreCase) >= 0,
                "The death event context did not mark itself a neutral death description.");

            // No first-person pairing: a neutral death page has no recipient POV.
            PawnDiaryRimTestScope.Require(
                string.IsNullOrEmpty(deathEvent.recipientPawnId),
                "A neutral death description should not carry a recipient pawn.");

            // (a) The killed colonist's diary references the death page at its final boundary. This is the
            // same death predicate the fallback and Tale routes use to avoid double-writing, so it also
            // confirms the event id the harness cleanup will remove is filed under the victim.
            PawnDiaryRimTestScope.Require(
                scope.Component.HasDeathDescriptionFor(victim),
                "The killed colonist's diary did not reference a neutral death description.");
        }

        /// <summary>
        /// EVT-10. Cross-source dedup: after a real kill has recorded the one neutral death page, a
        /// further death source for the same pawn (a redundant DeathFallbackSignal, the same shared
        /// DeathDescriptionKey / HasExistingDeathDescription guard the Tale death route also honors) is
        /// dropped — the death Tale and the Pawn.Kill fallback can never both produce a page.
        /// </summary>
        [Test]
        public static void SecondDeathSourceDoesNotAddDuplicatePage()
        {
            RegisterDeadPawnCleanup(victim);

            // The real Pawn.Kill fallback records the one neutral death page.
            scope.FireAndRequireEvent(
                () => victim.Kill(null),
                DeathFallbackSignal.DeathFallbackDefName,
                victim,
                null);

            // A death page now exists for this pawn. Submitting another death source for it must record
            // nothing: the fallback signal reads HasExistingDeathDescription and the dispatcher's shared
            // DeathDescription dedup key both block a second final page.
            scope.RequireNoNewEvent(() => DiaryEvents.Submit(new DeathFallbackSignal(victim)));

            // The pawn still has exactly its single neutral death description.
            PawnDiaryRimTestScope.Require(
                scope.Component.HasDeathDescriptionFor(victim),
                "The killed colonist should still have exactly its one neutral death description.");
        }

        // ----- test helpers -----------------------------------------------------------------------

        /// <summary>
        /// Registers cleanup for the state a killed pawn leaves that the harness does not already own.
        /// Pawn.Kill can move the pawn into a (possibly unspawned) <see cref="Corpse"/> holder, and a dead
        /// colonist can be passed to the world-pawn registry; both are removed here so no corpse Thing or
        /// world pawn survives the test. The harness then destroys the pawn itself. Registered so the
        /// corpse is destroyed first (freeing/destroying the pawn), then any world-pawn entry is dropped.
        /// </summary>
        private static void RegisterDeadPawnCleanup(Pawn pawn)
        {
            // Registered first → runs last: drop a world-pawn entry only if one survived the corpse step.
            scope.RegisterCleanup(() =>
            {
                if (pawn != null
                    && !pawn.Destroyed
                    && Find.WorldPawns != null
                    && Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.RemovePawn(pawn);
                }
            });

            // Registered last → runs first: destroy the corpse holder so no corpse Thing leaks onto a map.
            scope.RegisterCleanup(() =>
            {
                Corpse corpse = pawn?.ParentHolder as Corpse;
                if (corpse != null && !corpse.Destroyed)
                {
                    corpse.Destroy(DestroyMode.Vanish);
                }
            });
        }
    }
}

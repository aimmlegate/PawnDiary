// In-game raid-capture tests for Pawn Diary's colony raid source (EVT-13, design/TEST_COVERAGE_PLAN.md §3).
//
// A raid is a colony-wide FAN-OUT: the RaidExecutePatch builds one RaidFanoutSignal from the incident's
// IncidentParms + IncidentDef and submits it, and the shared DiaryFanoutSignal dispatch turns it into one
// solo diary page per eligible colonist on the raid's target map, under a single colony-level dedup window
// (Source/Ingestion/Sources/RaidSignal.cs, Source/Core/DiaryGameComponent.Dispatch.cs).
//
// The real fan-out iterates map.mapPawns.FreeColonists — the PLAYER'S live colonists — so running the whole
// fan-out through DiaryEvents.Submit(fanout) on the current map would write raid pages into real colonists'
// diaries, which no harness could clean. So, exactly like the Work suite drives WorkSignal per-pawn instead
// of the whole periodic scan, these tests build a valid RaidFanoutSignal (a real Find.CurrentMap is needed
// for validity — guarded) and then drive the exact per-colonist unit the fan-out yields for the ONE isolated
// test pawn: DiaryEvents.Submit(new RaidPawnSignal(fanout, pawn, id)). That is what PerPawnSignals() produces
// for each eligible colonist, kept to a single test-owned pawn so the harness can clean it.
//
// Coverage split (all deterministic, no random loops, no LLM — generation stays disabled on the test pawn):
//   (a) per-colonist fan-out product: the per-pawn signal records one solo raid page with the raid context;
//   (b) colony dedup: the colony dedup key is stable + XML-tuned, an off-map (caravan/world) raid is excluded
//       and consumes no window, and a repeat raid whose colony window is already open emits nothing;
//   (c) bypass classes: drop-pod raids and infestations bypass the ordinary generation delay (pure
//       ShouldDelayGeneration) and classify to their own distinct groups rather than the raid catch-all.
//
// The per-pawn Emit routes through DelaySolo when raidGenerationDelayTicks > 0, which stamps the transient
// delayedRaidGenerationReadyTicks store (keyed by eventId, NOT pawn id — the shared harness cannot scrub it).
// SetUp forces raidGenerationDelayTicks = 0 so every emitted raid page takes the proven-safe QueueSolo path
// (the same path the other suites exercise), never touching that store. New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that Pawn Diary's colony raid source records the expected per-colonist solo page, honors its
    /// colony-level dedup window, and routes/bypasses the documented immediate-threat raid classes. These
    /// tests require a loaded game (the capture pipeline ignores events at the main menu) and a current map
    /// (a raid without a target map is not diary-worthy); both are guarded.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryRaidFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // Reflection handles for the two DiaryGameComponent internals the colony-dedup gate test needs but the
        // shared harness does not expose: the transient dedup store and the private mark primitive. Resolved
        // once; a null handle means the field/method was renamed and the test fails loudly rather than silently.
        private static readonly FieldInfo RecentEventsField =
            typeof(DiaryGameComponent).GetField("recentEvents", PrivateInstance);
        private static readonly FieldInfo EventsField =
            typeof(DiaryGameComponent).GetField("events", PrivateInstance);
        private static readonly MethodInfo MarkRecentlyRecordedMethod =
            typeof(DiaryGameComponent).GetMethod("MarkRecentlyRecorded", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn raidPawn;

        // The live tuning def whose raid generation delay we pin to 0 for the suite, plus the value to restore.
        private static DiaryTuningDef tuningDef;
        private static int originalRaidGenerationDelayTicks;

        /// <summary>
        /// Opens a scope with the catch-all "raid" group enabled (where an ordinary RaidEnemy classifies),
        /// creates one isolated generation-disabled colonist, and forces the raid generation delay to 0 so
        /// every emitted raid page takes the QueueSolo path (never the transient delay store). The tuning
        /// mutation is snapshotted and restored in teardown, so the developer's live tuning is untouched.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("raid");
            raidPawn = scope.CreateAdultColonist();

            tuningDef = DiaryTuning.Current;
            originalRaidGenerationDelayTicks = tuningDef.raidGenerationDelayTicks;
            tuningDef.raidGenerationDelayTicks = 0;
            scope.RegisterCleanup(() =>
            {
                if (tuningDef != null)
                {
                    tuningDef.raidGenerationDelayTicks = originalRaidGenerationDelayTicks;
                }
            });
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, or dedup key survived — even
        /// when a test above threw partway through.
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
                raidPawn = null;
                tuningDef = null;
            }
        }

        /// <summary>
        /// EVT-13. The per-colonist unit of the raid fan-out (RaidPawnSignal, exactly what PerPawnSignals
        /// yields for each eligible colonist) records one solo raid page for the pawn, carrying the raid
        /// game-context marker (incident, faction sentinel, points).
        /// </summary>
        [Test]
        public static void RaidFanOutPerColonistRecordsSoloRaidPage()
        {
            Map map = RequireCurrentMap();
            IncidentDef raidEnemy = RequireIncident("RaidEnemy");
            RaidFanoutSignal fanout = BuildRaidFanout(raidEnemy, map, 240f);
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrEmpty(fanout.ColonyDedupKey),
                "A RaidEnemy on the current map must be a diary-worthy raid (is the 'raid' group enabled?).");

            RaidPawnSignal perPawn = new RaidPawnSignal(fanout, raidPawn, raidPawn.GetUniqueLoadID());

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(perPawn),
                "RaidEnemy",
                raidPawn,
                null);

            scope.RequireSoloRef(diaryEvent, raidPawn);
            RequireContextContains(diaryEvent, "raid=RaidEnemy");
            RequireContextContains(diaryEvent, "faction=" + RaidEventData.FactionUnknown);
            RequireContextContains(diaryEvent, "points=240");
        }

        /// <summary>An existing raid page may gain exact raid doctrine but remains the sole owner.</summary>
        [Test]
        public static void RaidPageSelectsOnlyRelevantLiveDoctrine()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message("[PawnDiary RimTest Raid] belief enrichment: not applicable (Ideology inactive). ");
                return;
            }

            InstallOnlyIssueStance(raidPawn, "Raiding_Respected");
            Map map = RequireCurrentMap();
            RaidFanoutSignal fanout = BuildRaidFanout(RequireIncident("RaidEnemy"), map, 240f);
            RaidPawnSignal perPawn = new RaidPawnSignal(
                fanout, raidPawn, raidPawn.GetUniqueLoadID());
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(perPawn), "RaidEnemy", raidPawn, null);
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(
                    diaryEvent.BeliefContextForRole(DiaryEvent.InitiatorRole)),
                "The existing raid page did not select the installed live raiding stance.");
            scope.RequireSoloRef(diaryEvent, raidPawn);
        }

        /// <summary>
        /// EVT-13. Colony dedup contract: two identical raids on the same map collapse to one non-empty
        /// colony dedup key whose window is the XML-tuned raidDedupTicks; a raid with no target map
        /// (a caravan/world threat) is excluded — empty key AND zero per-pawn fan-out, so it consumes no window.
        /// </summary>
        [Test]
        public static void IdenticalRaidsShareOneWindowAndOffMapRaidsAreExcluded()
        {
            Map map = RequireCurrentMap();
            IncidentDef raidEnemy = RequireIncident("RaidEnemy");

            RaidFanoutSignal first = BuildRaidFanout(raidEnemy, map, 300f);
            RaidFanoutSignal second = BuildRaidFanout(raidEnemy, map, 300f);

            PawnDiaryRimTestScope.Require(
                !string.IsNullOrEmpty(first.ColonyDedupKey),
                "A diary-worthy raid should expose a non-empty colony dedup key.");
            PawnDiaryRimTestScope.Require(
                string.Equals(first.ColonyDedupKey, second.ColonyDedupKey, StringComparison.Ordinal),
                "Two identical raids on the same map must collapse to one colony dedup window.");
            PawnDiaryRimTestScope.Require(
                first.ColonyDedupTicks == DiaryTuning.Current.raidDedupTicks && first.ColonyDedupTicks > 0,
                "The raid colony dedup window must be the XML-tuned raidDedupTicks and active (>0).");

            // Off-map raid: a caravan/world threat has no target Map, so it is not diary-worthy — no colony
            // window is opened and no per-pawn pages are produced.
            IncidentParms offMap = new IncidentParms { target = null, points = 300f };
            RaidFanoutSignal offMapRaid = new RaidFanoutSignal(offMap, raidEnemy);
            PawnDiaryRimTestScope.Require(
                string.IsNullOrEmpty(offMapRaid.ColonyDedupKey),
                "A raid with no target map must not consume a colony dedup window.");

            int childCount = 0;
            foreach (var child in offMapRaid.PerPawnSignals())
            {
                childCount++;
            }

            PawnDiaryRimTestScope.Require(
                childCount == 0,
                "A raid with no target map must fan out to no colonists.");
        }

        /// <summary>
        /// EVT-13. Colony dedup gate: with the raid's colony window already open (pre-seeded exactly as
        /// production marks it), the very next Dispatch of that raid short-circuits BEFORE the per-colonist
        /// loop and emits nothing — the whole fan-out is suppressed, so a single raid never doubles per colonist.
        /// The colony dedup key carries no pawn id, so the shared harness scrub cannot remove it; this test
        /// cleans the seeded key itself (see harnessExtensionsNeeded).
        /// </summary>
        [Test]
        public static void OpenColonyDedupWindowSuppressesTheRepeatRaidFanOut()
        {
            Map map = RequireCurrentMap();
            IncidentDef raidEnemy = RequireIncident("RaidEnemy");
            RaidFanoutSignal fanout = BuildRaidFanout(raidEnemy, map, 180f);

            string colonyKey = fanout.ColonyDedupKey;
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrEmpty(colonyKey),
                "A diary-worthy raid should expose a non-empty colony dedup key to seed.");

            SeedColonyDedupKey(colonyKey, fanout.ColonyDedupTicks);

            int before = TotalEventCount();
            scope.Component.Dispatch(fanout);
            int after = TotalEventCount();

            PawnDiaryRimTestScope.Require(
                before == after,
                "A raid whose colony dedup window is already open must emit no new entries (the whole fan-out is suppressed).");
        }

        /// <summary>
        /// EVT-13. Documented bypass: an ordinary approaching raid waits out the XML anticipation delay, while
        /// the immediate-threat classes (infestations, drop-pod arrivals, drop strategies) and a non-positive
        /// delay all bypass the wait. This is the pure ShouldDelayGeneration policy the raid Emit consults.
        /// </summary>
        [Test]
        public static void ImmediateThreatRaidsBypassTheGenerationDelay()
        {
            PawnDiaryRimTestScope.Require(
                RaidEventData.ShouldDelayGeneration("RaidEnemy", "EdgeWalkIn", null, 2500),
                "An ordinary edge-walk-in raid with a positive delay should wait before generating.");
            PawnDiaryRimTestScope.Require(
                !RaidEventData.ShouldDelayGeneration("Infestation", null, null, 2500),
                "An infestation is an immediate internal threat and must bypass the generation delay.");
            PawnDiaryRimTestScope.Require(
                !RaidEventData.ShouldDelayGeneration("RaidEnemy", "EdgeDrop", null, 2500),
                "A drop-pod-arrival raid lands inside the colony and must bypass the generation delay.");
            PawnDiaryRimTestScope.Require(
                !RaidEventData.ShouldDelayGeneration("RaidEnemy", null, "StageThenAttackDrop", 2500),
                "A drop-pod strategy must bypass the generation delay.");
            PawnDiaryRimTestScope.Require(
                !RaidEventData.ShouldDelayGeneration("RaidEnemy", "EdgeWalkIn", null, 0),
                "A non-positive delay tuning disables the anticipation wait entirely.");
        }

        /// <summary>
        /// EVT-13. Classification: an ordinary RaidEnemy falls through to the catch-all "raid" group, while
        /// the documented bypass classes route to their OWN distinct groups (raidInfestation / raidDropPod),
        /// i.e. they are excluded from the generic raid catch-all.
        /// </summary>
        [Test]
        public static void BypassRaidClassesRouteToDistinctGroups()
        {
            DiaryInteractionGroupDef ordinary = InteractionGroups.ClassifyRaid("RaidEnemy");
            DiaryInteractionGroupDef infestation = InteractionGroups.ClassifyRaid("Infestation");
            DiaryInteractionGroupDef dropPod = InteractionGroups.ClassifyRaid("RaidEnemy; arrival=EdgeDrop");

            PawnDiaryRimTestScope.Require(
                ordinary != null && string.Equals(ordinary.defName, "raid", StringComparison.Ordinal),
                "An ordinary RaidEnemy should classify to the catch-all 'raid' group.");
            PawnDiaryRimTestScope.Require(
                infestation != null && string.Equals(infestation.defName, "raidInfestation", StringComparison.Ordinal),
                "An infestation should classify to its own 'raidInfestation' group, not the raid catch-all.");
            PawnDiaryRimTestScope.Require(
                dropPod != null && string.Equals(dropPod.defName, "raidDropPod", StringComparison.Ordinal),
                "A drop-pod raid should classify to its own 'raidDropPod' group, not the raid catch-all.");
        }

        // ----- test helpers -----------------------------------------------------------------------

        /// <summary>
        /// Builds a valid colony raid fan-out for the given incident on the given map. Faction is left unset
        /// (the source substitutes its English "unknown" sentinel) and no arrival mode/strategy is supplied,
        /// so an ordinary RaidEnemy classifies to the catch-all "raid" group.
        /// </summary>
        private static RaidFanoutSignal BuildRaidFanout(IncidentDef incidentDef, Map map, float points)
        {
            IncidentParms parms = new IncidentParms { target = map, points = points };
            return new RaidFanoutSignal(parms, incidentDef);
        }

        private static void InstallOnlyIssueStance(Pawn pawn, string preceptDefName)
        {
            PreceptDef target = DefDatabase<PreceptDef>.GetNamedSilentFail(preceptDefName);
            PawnDiaryRimTestScope.Require(pawn?.ideo != null && target?.issue != null,
                "The raid belief fixture needs vanilla precept " + preceptDefName + ".");
            Ideo ideology = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms
            {
                forFaction = Faction.OfPlayer.def,
                fixedIdeo = true
            });
            PawnDiaryRimTestScope.Require(ideology != null,
                "RimWorld did not generate the disposable raid ideoligion.");
            foreach (Precept stance in ideology.PreceptsListForReading
                .Where(precept => precept?.def?.issue != null).ToList())
                ideology.RemovePrecept(stance, true);
            ideology.AddPrecept(PreceptMaker.MakePrecept(target), false, Faction.OfPlayer.def, null);
            pawn.ideo.SetIdeo(ideology);
            PawnDiaryRimTestScope.Require(
                ideology.PreceptsListForReading.Count(precept => precept?.def?.issue != null) == 1,
                "The disposable raid ideoligion retained an unrelated issue stance.");
        }

        /// <summary>
        /// Seeds the colony dedup window for a raid EXACTLY as production does (via the private
        /// MarkRecentlyRecorded), then registers cleanup that removes the seeded key. The key carries no pawn
        /// id, so the shared harness's key-contains-pawn-id scrub cannot see it.
        /// </summary>
        private static void SeedColonyDedupKey(string colonyKey, int windowTicks)
        {
            if (RecentEventsField == null || MarkRecentlyRecordedMethod == null)
            {
                throw new AssertionException(
                    "Pawn Diary raid test could not locate DiaryGameComponent.recentEvents / MarkRecentlyRecorded.");
            }

            object recentEvents = RecentEventsField.GetValue(scope.Component);
            if (recentEvents == null)
            {
                throw new AssertionException("DiaryGameComponent.recentEvents was null; cannot seed the colony dedup window.");
            }

            MarkRecentlyRecordedMethod.Invoke(scope.Component, new object[] { recentEvents, colonyKey, windowTicks });
            scope.RegisterCleanup(() => (recentEvents as IDictionary)?.Remove(colonyKey));
        }

        private static int TotalEventCount()
        {
            if (EventsField == null)
            {
                throw new AssertionException("Pawn Diary raid test could not locate DiaryGameComponent.events.");
            }

            DiaryEventRepository repository = EventsField.GetValue(scope.Component) as DiaryEventRepository;
            if (repository == null)
            {
                throw new AssertionException("DiaryGameComponent.events was not a DiaryEventRepository.");
            }

            return repository.AllEvents.Count;
        }

        private static Map RequireCurrentMap()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                throw new AssertionException("EVT-13 raid fan-out needs a loaded map (Find.CurrentMap was null).");
            }

            return map;
        }

        private static IncidentDef RequireIncident(string defName)
        {
            IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                throw new AssertionException("Required vanilla IncidentDef '" + defName + "' was not loaded.");
            }

            return def;
        }

        private static void RequireContextContains(DiaryEvent diaryEvent, string expectedFragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(expectedFragment, StringComparison.Ordinal) >= 0,
                "The raid event context did not contain the expected fact '" + expectedFragment + "'.");
        }
    }
}

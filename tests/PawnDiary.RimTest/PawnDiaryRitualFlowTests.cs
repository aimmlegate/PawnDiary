// In-game ritual-capture tests for Pawn Diary's Ideology/psychic-ritual fan-out (EVT-17).
//
// A finished ritual is a colony perspective FAN-OUT: RitualFanoutSignal (Ideology,
// LordJob_Ritual.ApplyOutcome) and PsychicRitualFanoutSignal (Anomaly, psychic ritual) each emit one
// solo diary entry per organizer/invoker, target, participant, and spectator, sharing one ritual-level
// dedup window, and each perspective carries its own localized role label, instruction, and
// "ritual="/"psychic_ritual=" game-context marker.
//
// Live vanilla ritual objects cannot be safely constructed mapless, so the suite uses the production
// signals' internal fact-based fixture seams. Those seams bypass only reflection over LordJob_Ritual /
// PsychicRitual; they still drive the real fan-out order, uniqueness filter, colony dedup, child
// capture decision, AddSoloEvent persistence, diary references, and prompt-context assembly.
//
// The suite proves in-game, deterministically, with no map and without owning any DLC:
//   * both ritual fan-outs emit one unique page per organizer/invoker, target, participant, and
//     spectator, and a second dispatch is suppressed by the shared ritual-level dedup window;
//   * the per-perspective game-context markers the prompt receives are well-formed and field-ordered,
//     with the ritual quality label drawn from live XML tuning (DiaryTuningDef.ritualQualityBands);
//   * every organizer/target/participant/spectator (and psychic invoker) role LABEL and INSTRUCTION
//     translation key resolves to a non-empty, distinct string in the loaded language data — the
//     fan-out's localization contract, which the out-of-game policy tests cannot exercise;
//   * the DLC-gated context fields the ritual marker embeds (royal title, ideological role) stay
//     base-game safe: absent DLC yields empty accessors and a clean "none" fallback, never a crash.
//
// Coverage-matrix ID (TEST_COVERAGE_PLAN.md §3): EVT-17 Ritual (DLC).
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the Ideology/psychic ritual fan-outs' unique per-perspective pages, colony dedup,
    /// context, localization, and DLC-safe fields in a loaded game. Fact fixtures bypass only unsafe
    /// construction of live ritual jobs; the production fan-out/child pipeline remains under test.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryRitualFlowTests
    {
        // A synthetic ritual identity. The fan-out stores whatever defName/title the live ritual had;
        // these tests exercise the pure/localization surface, so any stable non-empty values work.
        private const string RitualDefName = "RimTest_Ritual";
        private const string RitualTitle = "RimTest Ceremony";
        private const string RitualBehavior = "RitualBehaviorWorker_RimTest";

        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static readonly FieldInfo EventsField = typeof(DiaryGameComponent).GetField(
            "events", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Opens a fresh test scope and creates one isolated adult colonist (generation disabled) for the
        /// DLC-context assertions. No interaction groups are needed: this suite never drives the
        /// group-gated capture constructor.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            firstPawn = scope.CreateAdultColonist();
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned state survived — even when the test
        /// above threw partway through.
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
            }
        }

        /// <summary>
        /// EVT-17. Verifies the Ideology ritual game-context marker the fan-out hands the prompt: the
        /// load-bearing "ritual=" prefix, the locked field order, the per-perspective "ritual_perspective="
        /// field, the finished outcome, and a quality label taken from live XML tuning
        /// (DiaryTuningDef.ritualQualityBands) rather than a hardcoded band.
        /// </summary>
        [Test]
        public static void IdeologyRitualContextMarkerUsesLiveTuningAndLockedFieldOrder()
        {
            // Quality label is computed by the SAME production path the fan-out uses, against the live,
            // XML-loaded bands. 0.9f is a fixed input so the resulting label is deterministic.
            string quality = RitualEventData.QualityLabel(0.9f, DiaryTuning.Current.ritualQualityBands);
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(quality),
                "Live ritual quality bands produced no label for a 0.9 ritual.");

            string context = RitualEventData.BuildGameContext(
                RitualDefName,
                RitualTitle,
                RitualBehavior,
                RitualEventData.PerspectiveOrganizer,
                "speaker",
                royalTitle: string.Empty,
                ideologicalRole: string.Empty,
                RitualFanoutSignal.RitualOutcomeFinished,
                quality);

            PawnDiaryRimTestScope.Require(
                context.StartsWith("ritual=" + RitualDefName, StringComparison.Ordinal),
                "The ritual context must lead with the load-bearing 'ritual=<defName>' marker.");
            RequireContains(context, "ritual_title=" + RitualTitle);
            RequireContains(context, "ritual_behavior=" + RitualBehavior);
            RequireContains(context, "ritual_perspective=" + RitualEventData.PerspectiveOrganizer);
            RequireContains(context, "outcome=" + RitualFanoutSignal.RitualOutcomeFinished);
            RequireContains(context, "quality=" + quality);

            // Field order is part of the contract (locked by the out-of-game tests too). Confirm the
            // marker sequence in-game so a re-order that still compiles is caught here as well.
            RequireOrdered(context, "ritual=", "ritual_title=", "ritual_behavior=", "ritual_perspective=",
                "ritual_role=", "royal_title=", "ideological_role=", "outcome=", "quality=");
        }

        /// <summary>
        /// EVT-17. Verifies the Ideology fan-out's per-perspective localization contract: each of the
        /// organizer/target/participant/spectator perspectives resolves to a non-empty role label AND a
        /// non-empty role instruction in the loaded language, and the four role labels are pairwise
        /// distinct so no two perspectives collapse to the same prompt guidance.
        /// </summary>
        [Test]
        public static void IdeologyRitualPerspectiveLabelsAndInstructionsResolve()
        {
            string[] perspectives =
            {
                RitualEventData.PerspectiveOrganizer,
                RitualEventData.PerspectiveTarget,
                RitualEventData.PerspectiveParticipant,
                RitualEventData.PerspectiveSpectator,
            };

            string[] labels = new string[perspectives.Length];
            for (int i = 0; i < perspectives.Length; i++)
            {
                labels[i] = RitualFanoutSignal.RitualPerspectiveLabel(perspectives[i]);
                PawnDiaryRimTestScope.Require(
                    !string.IsNullOrWhiteSpace(labels[i]),
                    "Ritual perspective '" + perspectives[i] + "' resolved to an empty role label.");

                string instruction = RitualFanoutSignal.RitualInstructionFor(perspectives[i]);
                PawnDiaryRimTestScope.Require(
                    !string.IsNullOrWhiteSpace(instruction),
                    "Ritual perspective '" + perspectives[i] + "' resolved to an empty role instruction.");
            }

            RequirePairwiseDistinct(labels, "Ideology ritual role labels");
        }

        /// <summary>
        /// EVT-17. Verifies the Anomaly psychic-ritual fan-out's context + localization wiring, which is
        /// DLC-content-free to evaluate: the "psychic_ritual=" marker (deliberately WITHOUT the Ideology
        /// role/title fields) is well-formed, and each invoker/target/participant/spectator perspective
        /// resolves to a non-empty, pairwise-distinct label and a non-empty instruction. The live psychic
        /// event-creation fan-out itself is DLC-gated (see the no-op branch and productionSeamNeeded).
        /// </summary>
        [Test]
        public static void PsychicRitualContextMarkerAndPerspectiveWiringResolve()
        {
            string quality = RitualEventData.QualityLabel(0.9f, DiaryTuning.Current.ritualQualityBands);
            string context = RitualEventData.BuildPsychicGameContext(
                RitualDefName,
                RitualEventData.PerspectiveInvoker,
                RitualFanoutSignal.RitualOutcomeFinished,
                quality);

            PawnDiaryRimTestScope.Require(
                context.StartsWith("psychic_ritual=" + RitualDefName, StringComparison.Ordinal),
                "The psychic ritual context must lead with the 'psychic_ritual=<defName>' marker.");
            RequireContains(context, "psychic_ritual_perspective=" + RitualEventData.PerspectiveInvoker);
            RequireContains(context, "outcome=" + RitualFanoutSignal.RitualOutcomeFinished);
            RequireContains(context, "quality=" + quality);
            // Psychic context deliberately omits the Ideology-only role/title fields.
            PawnDiaryRimTestScope.Require(
                context.IndexOf("ritual_role=", StringComparison.Ordinal) < 0
                    && context.IndexOf("royal_title=", StringComparison.Ordinal) < 0,
                "The psychic ritual context must not carry the Ideology ritual role/title fields.");

            string[] perspectives =
            {
                RitualEventData.PerspectiveInvoker,
                RitualEventData.PerspectiveTarget,
                RitualEventData.PerspectiveParticipant,
                RitualEventData.PerspectiveSpectator,
            };

            string[] labels = new string[perspectives.Length];
            for (int i = 0; i < perspectives.Length; i++)
            {
                labels[i] = PsychicRitualFanoutSignal.PsychicRitualPerspectiveLabel(perspectives[i]);
                PawnDiaryRimTestScope.Require(
                    !string.IsNullOrWhiteSpace(labels[i]),
                    "Psychic ritual perspective '" + perspectives[i] + "' resolved to an empty label.");

                string instruction = PsychicRitualFanoutSignal.PsychicRitualInstructionFor(perspectives[i]);
                PawnDiaryRimTestScope.Require(
                    !string.IsNullOrWhiteSpace(instruction),
                    "Psychic ritual perspective '" + perspectives[i] + "' resolved to an empty instruction.");
            }

            RequirePairwiseDistinct(labels, "psychic ritual role labels");
        }

        /// <summary>
        /// EVT-17 / Ideology. Drives the production ritual fan-out from copied fixture facts through
        /// all four perspective pages. Duplicate role membership is deliberately supplied so the
        /// pawn-ID uniqueness filter and ritual-level second-dispatch dedup are both exercised.
        /// </summary>
        [Test]
        public static void IdeologyRitualFixtureFanoutEmitsUniquePerspectivePagesOnce()
        {
            Pawn target = scope.CreateAdultColonist();
            Pawn participant = scope.CreateAdultColonist();
            Pawn spectator = scope.CreateAdultColonist();
            HashSet<string> before = SnapshotEventIds();

            RitualFanoutSignal fanout = RitualFanoutSignal.CreateTestFixture(
                firstPawn,
                target,
                new List<Pawn> { firstPawn, participant },
                new List<Pawn> { participant, spectator },
                RitualDefName,
                RitualTitle,
                RitualBehavior,
                0.82f,
                "fixture ritual theme");
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(fanout.ColonyDedupKey),
                "The Ideology ritual fixture did not create a colony dedup key.");

            scope.Component.Dispatch(fanout);
            List<DiaryEvent> emitted = NewEventsSince(before);
            PawnDiaryRimTestScope.Require(emitted.Count == 4,
                "The Ideology ritual fixture should emit exactly four unique perspective pages, got "
                + emitted.Count + ".");
            RequirePerspectiveEvent(emitted, "ritual_perspective=" + RitualEventData.PerspectiveOrganizer, firstPawn);
            RequirePerspectiveEvent(emitted, "ritual_perspective=" + RitualEventData.PerspectiveTarget, target);
            RequirePerspectiveEvent(emitted, "ritual_perspective=" + RitualEventData.PerspectiveParticipant, participant);
            RequirePerspectiveEvent(emitted, "ritual_perspective=" + RitualEventData.PerspectiveSpectator, spectator);

            int afterFirst = EventRepository().Count;
            scope.Component.Dispatch(fanout);
            PawnDiaryRimTestScope.Require(EventRepository().Count == afterFirst,
                "A repeated Ideology ritual fan-out escaped the colony dedup window.");
        }

        /// <summary>
        /// EVT-17 / Anomaly. Drives the production psychic-ritual fan-out through invoker, target,
        /// participant, and spectator pages without constructing or firing a real colony ritual.
        /// Duplicate assignment rows must collapse by pawn ID and the repeat dispatch must be inert.
        /// </summary>
        [Test]
        public static void PsychicRitualFixtureFanoutEmitsUniquePerspectivePagesOnce()
        {
            Pawn target = scope.CreateAdultColonist();
            Pawn participant = scope.CreateAdultColonist();
            Pawn spectator = scope.CreateAdultColonist();
            HashSet<string> before = SnapshotEventIds();

            PsychicRitualFanoutSignal fanout = PsychicRitualFanoutSignal.CreateTestFixture(
                firstPawn,
                target,
                new List<Pawn> { firstPawn, participant },
                new List<Pawn> { participant, spectator },
                "VoidProvocation",
                "void provocation",
                0.91f,
                "fixture psychic ritual theme");
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(fanout.ColonyDedupKey),
                "The psychic ritual fixture did not create a colony dedup key.");

            scope.Component.Dispatch(fanout);
            List<DiaryEvent> emitted = NewEventsSince(before);
            PawnDiaryRimTestScope.Require(emitted.Count == 4,
                "The psychic ritual fixture should emit exactly four unique perspective pages, got "
                + emitted.Count + ".");
            RequirePerspectiveEvent(emitted, "psychic_ritual_perspective=" + RitualEventData.PerspectiveInvoker, firstPawn);
            RequirePerspectiveEvent(emitted, "psychic_ritual_perspective=" + RitualEventData.PerspectiveTarget, target);
            RequirePerspectiveEvent(emitted, "psychic_ritual_perspective=" + RitualEventData.PerspectiveParticipant, participant);
            RequirePerspectiveEvent(emitted, "psychic_ritual_perspective=" + RitualEventData.PerspectiveSpectator, spectator);

            int afterFirst = EventRepository().Count;
            scope.Component.Dispatch(fanout);
            PawnDiaryRimTestScope.Require(EventRepository().Count == afterFirst,
                "A repeated psychic ritual fan-out escaped the colony dedup window.");
        }

        /// <summary>
        /// EVT-17. The explicit DLC gate. The Ideology ritual context embeds each pawn's royal title
        /// (Royalty) and ideological role (Ideology) via the double-guarded DlcContext accessors. This
        /// asserts the base-game-safety contract those fields depend on: when the owning DLC is absent
        /// the accessor is a clean, non-null empty string (never a crash), and the assembled ritual
        /// marker degrades to the "none" fallback rather than a malformed field. When a DLC is present
        /// the accessor is still non-null (the test pawn simply holds no title/role). No state is created
        /// on either branch, so the no-op path is a true "not applicable" pass.
        /// </summary>
        [Test]
        public static void RitualDlcContextFieldsAreGatedAndBaseGameSafe()
        {
            string royalTitle = DlcContext.RoyalTitle(firstPawn);
            string ideologicalRole = DlcContext.IdeologicalRole(firstPawn);

            PawnDiaryRimTestScope.Require(
                royalTitle != null && ideologicalRole != null,
                "DLC context accessors must never return null for the ritual context.");

            if (!ModsConfig.RoyaltyActive)
            {
                PawnDiaryRimTestScope.Require(
                    royalTitle.Length == 0,
                    "Without Royalty, the ritual royal-title context must be empty (not applicable).");
            }

            if (!ModsConfig.IdeologyActive)
            {
                PawnDiaryRimTestScope.Require(
                    ideologicalRole.Length == 0,
                    "Without Ideology, the ritual ideological-role context must be empty (not applicable).");
            }

            // The assembled marker must stay well-formed even when both DLC fields are empty: the pure
            // builder substitutes the "none" fallback, so a base-game colony never emits a broken field.
            string context = RitualEventData.BuildGameContext(
                RitualDefName,
                RitualTitle,
                RitualBehavior,
                RitualEventData.PerspectiveParticipant,
                RitualFanoutSignal.RitualPerspectiveLabel(RitualEventData.PerspectiveParticipant),
                royalTitle,
                ideologicalRole,
                RitualFanoutSignal.RitualOutcomeFinished,
                RitualEventData.QualityLabel(0.5f, DiaryTuning.Current.ritualQualityBands));

            if (royalTitle.Length == 0)
            {
                RequireContains(context, "royal_title=none");
            }

            if (ideologicalRole.Length == 0)
            {
                RequireContains(context, "ideological_role=none");
            }
        }

        // ----- helpers ---------------------------------------------------------------------------

        private static HashSet<string> SnapshotEventIds()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<DiaryEvent> events = EventRepository().AllEvents;
            for (int i = 0; i < events.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(events[i]?.eventId))
                {
                    ids.Add(events[i].eventId);
                }
            }

            return ids;
        }

        private static List<DiaryEvent> NewEventsSince(HashSet<string> before)
        {
            List<DiaryEvent> result = new List<DiaryEvent>();
            IReadOnlyList<DiaryEvent> events = EventRepository().AllEvents;
            for (int i = 0; i < events.Count; i++)
            {
                DiaryEvent diaryEvent = events[i];
                if (diaryEvent != null && !before.Contains(diaryEvent.eventId))
                {
                    result.Add(diaryEvent);
                }
            }

            return result;
        }

        private static DiaryEventRepository EventRepository()
        {
            DiaryEventRepository repository = EventsField?.GetValue(scope.Component) as DiaryEventRepository;
            if (repository == null)
            {
                throw new AssertionException("EVT-17 could not read DiaryGameComponent.events.");
            }

            return repository;
        }

        private static void RequirePerspectiveEvent(
            List<DiaryEvent> events,
            string contextMarker,
            Pawn expectedPawn)
        {
            DiaryEvent match = null;
            for (int i = 0; i < events.Count; i++)
            {
                DiaryEvent candidate = events[i];
                if (candidate?.gameContext != null
                    && candidate.gameContext.IndexOf(contextMarker, StringComparison.Ordinal) >= 0)
                {
                    PawnDiaryRimTestScope.Require(match == null,
                        "More than one ritual page carried perspective marker '" + contextMarker + "'.");
                    match = candidate;
                }
            }

            PawnDiaryRimTestScope.Require(match != null,
                "No ritual page carried perspective marker '" + contextMarker + "'.");
            scope.RequireSoloRef(match, expectedPawn);
        }

        private static void RequireContains(string haystack, string needle)
        {
            PawnDiaryRimTestScope.Require(
                haystack != null && haystack.IndexOf(needle, StringComparison.Ordinal) >= 0,
                "Expected the ritual context to contain '" + needle + "', but it was: " + haystack);
        }

        // Asserts the given markers appear in the string in strictly increasing position order. This
        // guards the field-order contract the prompt policies and downstream tests rely on.
        private static void RequireOrdered(string haystack, params string[] markers)
        {
            int previous = -1;
            for (int i = 0; i < markers.Length; i++)
            {
                int at = haystack == null ? -1 : haystack.IndexOf(markers[i], StringComparison.Ordinal);
                PawnDiaryRimTestScope.Require(
                    at > previous,
                    "Ritual context field '" + markers[i] + "' was missing or out of order in: " + haystack);
                previous = at;
            }
        }

        private static void RequirePairwiseDistinct(string[] values, string label)
        {
            for (int i = 0; i < values.Length; i++)
            {
                for (int j = i + 1; j < values.Length; j++)
                {
                    PawnDiaryRimTestScope.Require(
                        !string.Equals(values[i], values[j], StringComparison.Ordinal),
                        "Two " + label + " collapsed to the same string ('" + values[i] + "').");
                }
            }
        }
    }
}

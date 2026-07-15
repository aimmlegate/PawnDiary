// Shared setup/teardown harness for Pawn Diary's in-game RimTest suites (TEST_COVERAGE_PLAN.md §2.1).
//
// Every loaded-game test needs the same fragile scaffolding: build isolated colonists that can never
// fire an LLM request, mutate a few settings, drive a real vanilla choke point, assert the persisted
// DiaryEvent, and then remove ALL of that test state — even when an assertion throws partway through.
// RimTest Redux only guarantees that a [AfterEach] method runs; it does not guarantee the cleanup
// inside it survives one failing step. This class is that guarantee: one scope owns the snapshots and
// runs every cleanup step through a failure accumulator, so a broken assertion can never strand a test
// pawn, relation, Social-log row, dedup key, or saved diary reference in the developer's live colony.
//
// New to C#/RimWorld? A few idioms used below:
//   - `BindingFlags` + `GetField(...)` is reflection: reading a class's PRIVATE fields by name at
//     runtime, the C# equivalent of reaching into another module's non-exported internals. The
//     production DiaryGameComponent keeps its stores private; the test assembly is trusted via
//     `[InternalsVisibleTo]` for `internal` members but still needs reflection for truly private ones.
//   - `Rand.PushState()` / `Rand.PopState()` save and restore RimWorld's global RNG cursor, so any
//     randomness the fired production code consumes never perturbs the player's own game stream.
//   - `Destroy(DestroyMode.Vanish)` removes a spawned/again-unspawned pawn with no corpse or side effects.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimTestRedux;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// One test's worth of isolated Pawn Diary state. Create it in <c>[BeforeEach]</c> with
    /// <see cref="Begin"/>, use its pawn factory and assertion helpers inside the test, and call
    /// <see cref="TearDown"/> in <c>[AfterEach]</c>. Teardown restores every mutation this scope made
    /// and then audits that no test-owned event, diary, or log row survived.
    /// </summary>
    internal sealed class PawnDiaryRimTestScope
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // Cached reflection handles for DiaryGameComponent's private stores. Resolved once and shared
        // across every scope; a null handle means the field was renamed and the harness must fail loudly
        // rather than silently skip its cleanup.
        private static readonly FieldInfo EventsField =
            typeof(DiaryGameComponent).GetField("events", PrivateInstance);
        private static readonly FieldInfo ArchiveField =
            typeof(DiaryGameComponent).GetField("archive", PrivateInstance);
        private static readonly FieldInfo DiariesField =
            typeof(DiaryGameComponent).GetField("diaries", PrivateInstance);
        private static readonly FieldInfo DiariesByIdField =
            typeof(DiaryGameComponent).GetField("diariesById", PrivateInstance);
        private static readonly FieldInfo RecentEventsField =
            typeof(DiaryGameComponent).GetField("recentEvents", PrivateInstance);
        private static readonly FieldInfo CommandStatusField =
            typeof(DiaryGameComponent).GetField("commandStatusByPawnId", PrivateInstance);

        // Additional pawn-scoped in-memory stores that accumulate between events and are NOT part of the
        // saved event/diary graph. Every one of these is keyed by a string that embeds the pawn's
        // GetUniqueLoadID (interaction pair/ambient keys, "pawnId|category" progression keys, and the
        // DaySummaryKey(pawnId, day) hediff key), so the same key-contains-pawnId scrub the harness uses
        // for recentEvents/commandStatus cleans them too. Resolved defensively — a null handle (field
        // renamed) simply degrades to not scrubbing that one store rather than crashing teardown.
        private static readonly FieldInfo PendingInteractionBatchesField =
            typeof(DiaryGameComponent).GetField("pendingInteractionBatches", PrivateInstance);
        private static readonly FieldInfo PendingAmbientNotesField =
            typeof(DiaryGameComponent).GetField("pendingAmbientInteractionNotes", PrivateInstance);
        private static readonly FieldInfo WrittenAmbientNotesField =
            typeof(DiaryGameComponent).GetField("writtenAmbientInteractionNotes", PrivateInstance);
        private static readonly FieldInfo ActiveThoughtProgressionsField =
            typeof(DiaryGameComponent).GetField("activeThoughtProgressions", PrivateInstance);
        private static readonly FieldInfo PendingDayHediffsField =
            typeof(DiaryGameComponent).GetField("pendingDayHediffs", PrivateInstance);
        private static readonly FieldInfo WrittenDayReflectionsField =
            typeof(DiaryGameComponent).GetField("writtenDayReflections", PrivateInstance);

        // Prompt Test Mode (Prefs.DevMode + settings.promptTestMode) makes QueuePrompt run the full
        // production resolution + render, stamp the assembled prompt on the event, mark it PromptOnly,
        // and return BEFORE any LlmClient.Enqueue — so a captured prompt is exactly what the runtime
        // would have sent, with no network. The rendered prompt lands in the private DiaryEvent.PromptFor
        // slot; these handles read it (and the POV status) back for assertions.
        private static readonly MethodInfo PromptForMethod =
            typeof(DiaryEvent).GetMethod("PromptFor", PrivateInstance);
        private static readonly MethodInfo StatusForMethod =
            typeof(DiaryEvent).GetMethod("StatusFor", PrivateInstance);

        // Per-scope owned state. Pawn ids are captured at creation time and used for cleanup/audit so
        // teardown still works after the pawn objects are destroyed.
        private readonly List<Pawn> testPawns = new List<Pawn>();
        private readonly List<string> testPawnIds = new List<string>();
        private readonly HashSet<LogEntry> addedPlayLogEntries = new HashSet<LogEntry>();
        private readonly List<Action> customCleanups = new List<Action>();
        private Dictionary<string, bool> originalGroupEnabled;
        private bool randPushed;

        // Prompt-capture mode state, snapshotted so teardown restores the developer's real settings.
        private bool promptCaptureEnabled;
        private bool originalDevMode;
        private bool originalPromptTestMode;
        private PromptContextDetailLevel originalContextDetailLevel;

        /// <summary>The loaded game component under test.</summary>
        public DiaryGameComponent Component { get; private set; }

        /// <summary>The active player faction the test pawns join.</summary>
        public Faction PlayerFaction { get; private set; }

        private PawnDiaryRimTestScope()
        {
        }

        /// <summary>
        /// Validates the loaded-game preconditions, snapshots the settings this harness may change,
        /// isolates the RNG, and enables the named automatic-capture groups. Throws
        /// <see cref="AssertionException"/> (surfaced in RimTest's result view) if the game, component,
        /// settings, player faction, or a required reflection handle is missing.
        /// </summary>
        /// <param name="groupsToEnable">Interaction-group keys to force on for this test.</param>
        public static PawnDiaryRimTestScope Begin(params string[] groupsToEnable)
        {
            if (!DiaryGameComponent.GamePlaying || Verse.Current.Game == null)
            {
                throw new AssertionException("Pawn Diary loaded-game tests require a loaded RimWorld game.");
            }

            DiaryGameComponent component = DiaryGameComponent.Instance;
            if (component == null || PawnDiaryMod.Settings == null)
            {
                throw new AssertionException(
                    "Pawn Diary's game component and settings must be loaded before this suite runs.");
            }

            Faction playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction == null)
            {
                throw new AssertionException("The loaded game has no player faction.");
            }

            RequireReflectionField(EventsField, "events");
            RequireReflectionField(ArchiveField, "archive");
            RequireReflectionField(DiariesField, "diaries");
            RequireReflectionField(DiariesByIdField, "diariesById");

            PawnDiaryRimTestScope scope = new PawnDiaryRimTestScope
            {
                Component = component,
                PlayerFaction = playerFaction,
                originalGroupEnabled = new Dictionary<string, bool>(
                    PawnDiaryMod.Settings.groupEnabled,
                    StringComparer.OrdinalIgnoreCase),
            };

            // Isolate the RNG BEFORE any capture runs so nothing the fired events roll (group tone
            // rotation, humor, staggered intensity) advances the player's own random stream.
            Rand.PushState();
            scope.randPushed = true;

            if (groupsToEnable != null)
            {
                for (int i = 0; i < groupsToEnable.Length; i++)
                {
                    PawnDiaryMod.Settings.SetGroupEnabled(groupsToEnable[i], true);
                }
            }

            return scope;
        }

        /// <summary>
        /// Generates an isolated adult colonist that is eligible for a first-person diary but cannot
        /// trigger an LLM request: the pawn's diary record is created while it is still factionless,
        /// generation is disabled, and only then is it made a colonist. The pawn is tracked for teardown.
        /// </summary>
        public Pawn CreateAdultColonist()
        {
            return CreateColonist(generationEnabled: false);
        }

        /// <summary>
        /// Like <see cref="CreateAdultColonist"/> but leaves diary generation ENABLED so a fired event
        /// runs the real prompt pipeline. Only valid after <see cref="EnablePromptCapture"/>: prompt-test
        /// mode makes the pipeline stamp the rendered prompt on the event and stop before any network
        /// call, so this never sends a real LLM request. Throws otherwise, as a safety rail.
        /// </summary>
        public Pawn CreateGeneratingAdultColonist()
        {
            if (!promptCaptureEnabled)
            {
                throw new AssertionException(
                    "Call EnablePromptCapture() before CreateGeneratingAdultColonist(), so a fired event is "
                    + "captured as prompt-only and can never send a real LLM request.");
            }

            return CreateColonist(generationEnabled: true);
        }

        private Pawn CreateColonist(bool generationEnabled)
        {
            PawnGenerationRequest request = new PawnGenerationRequest(
                PawnKindDefOf.Colonist,
                faction: null,
                context: PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                allowPregnant: false,
                allowAddictions: false,
                fixedBiologicalAge: 30f,
                developmentalStages: DevelopmentalStage.Adult,
                forceNoGear: true);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null)
            {
                throw new AssertionException("RimWorld failed to generate an isolated test pawn.");
            }

            // Record the pawn's diary while it is still factionless, set the generation flag, then make it
            // an eligible colonist. When generation is disabled, any SetFaction/arrival hook is unable to
            // reach an API. When it is enabled, prompt-test mode (required for that path) keeps the arrival
            // hook prompt-only, so it still cannot send a request.
            Component.SetDiaryGenerationEnabled(pawn, generationEnabled);
            pawn.SetFaction(PlayerFaction);
            if (!DiaryGameComponent.IsDiaryEligible(pawn))
            {
                throw new AssertionException(
                    "Generated test pawn did not satisfy Pawn Diary's colonist eligibility rules.");
            }

            testPawns.Add(pawn);
            testPawnIds.Add(pawn.GetUniqueLoadID());

            // The SetFaction above runs through the live game, so PawnSetFactionPatch.Postfix fires and
            // records a neutral "how I joined" arrival page for this pawn during setup. That is correct in
            // real play, but it is not state a test asked for: it pre-satisfies the arrival dedup (so a
            // test's own ArrivalSignal is dropped as a duplicate) and shows up as an extra ungenerated page
            // in any per-pawn count. Scrub it so every freshly created test pawn starts from a blank diary.
            ScrubSetupArrivalForPawn(pawn);
            return pawn;
        }

        /// <summary>
        /// Spawns a test pawn on the current map so generation's live-pawn lookup
        /// (<c>DiaryGameComponent.FindLivePawnByLoadId</c>, which scans map pawns + world pawns) can resolve
        /// it. The harness's pawns are generated but never spawned, so without this the generation pipeline
        /// sees a null pawn and silently drops every live-pawn-derived prompt layer (the hediff writing-style
        /// override, the humor temperament multiplier, the hediff prompt enchantment).
        ///
        /// Why spawn instead of <c>WorldPawns.PassToWorld</c>: passing a pawn to the world runs
        /// <c>PawnComponentsUtility.RemoveComponentsOnDespawned</c>, which strips the spawned-only
        /// components. A despawned world pawn is no longer a valid live colonist — vanilla
        /// <c>InspirationHandler.TryStartInspiration</c> refuses it (its <c>InspirationCanOccur</c> gate
        /// fails) and Pawn Diary's own <see cref="DiaryGameComponent.IsDiaryEligible"/> gate stops treating
        /// it as eligible, so SetPersona returns false and an ArrivalSignal is dropped. A map pawn is found
        /// by the same lookup AND stays a real colonist, so inspiration/arrival/persona behave as in play.
        /// Only prompt-capture suites that assert on live-pawn layers need it.
        ///
        /// Requires a loaded map (like the §4.2 death capture). Idempotent; teardown despawns and destroys
        /// the pawn (<see cref="DestroyTestPawns"/>).
        /// </summary>
        public void SpawnAsLiveColonist(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Spawned)
            {
                return;
            }

            Map map = Find.CurrentMap;
            if (map == null)
            {
                throw new AssertionException(
                    "This Pawn Diary prompt-capture suite needs a loaded map: its assertions read the "
                    + "writer's live state, so the test pawn must be spawned as a colonist.");
            }

            // Any standable, unfogged cell works; the pawn never ticks (it is destroyed in teardown before
            // the next map update), so pathing/placement quality is irrelevant. RNG is already isolated by
            // Begin's Rand.PushState, so the cell roll never perturbs the player's stream.
            IntVec3 cell;
            if (!CellFinder.TryFindRandomCellNear(map.Center, map, 40,
                    c => c.Standable(map) && !c.Fogged(map), out cell)
                && !CellFinder.TryFindRandomCell(map, c => c.Standable(map) && !c.Fogged(map), out cell))
            {
                cell = map.Center;
            }

            GenSpawn.Spawn(pawn, cell, map);
        }

        /// <summary>
        /// Guarantees a pawn satisfies vanilla <c>Inspired_Creativity</c>'s eligibility, the trigger the
        /// voice/humor/enchantment prompt-capture suites use. There is no "force" flag: in 1.6
        /// <c>TryStartInspiration(def, reason, sendLetter)</c>'s third argument is <c>sendLetter</c>, and the
        /// inspiration only starts when <c>InspirationWorker.InspirationCanOccur</c> genuinely passes. That
        /// def gates on <c>requiredAnySkill</c> (Construction/Artistic/Crafting &gt;= 3), a non-disabled
        /// qualifying work type, Manipulation, and minAge 13. A randomly generated colonist often rolls all
        /// three qualifying skills below 3, so raising the first non-disabled one above the threshold
        /// satisfies both the skill and (via that skill's work type) the work-type gate for any adult with
        /// at least one of those skills enabled. The remaining gates (IsColonist, Manipulation) hold for a
        /// generated adult and for one spawned via <see cref="SpawnAsLiveColonist"/>.
        /// </summary>
        public static void MakeCreativityInspirationEligible(Pawn pawn)
        {
            if (pawn?.skills == null)
            {
                throw new AssertionException("Cannot make a pawn without a skill set inspiration-eligible.");
            }

            SkillDef[] qualifying = { SkillDefOf.Artistic, SkillDefOf.Construction, SkillDefOf.Crafting };
            for (int i = 0; i < qualifying.Length; i++)
            {
                SkillRecord skill = pawn.skills.GetSkill(qualifying[i]);
                if (skill != null && !skill.TotallyDisabled)
                {
                    if (skill.Level < 4)
                    {
                        skill.Level = 4;
                    }

                    return;
                }
            }

            throw new AssertionException(
                "The generated test pawn had Construction, Artistic and Crafting all disabled, so it cannot "
                + "receive Inspired_Creativity; regenerate or choose a different capture trigger.");
        }

        /// <summary>
        /// Removes the neutral arrival page the Pawn.SetFaction hook recorded for a just-created test pawn:
        /// the hot event, its archived copy, the pawn's diary-index ref, and the transient dedup mark. The
        /// pawn's diary record itself is kept — only the incidental arrival goes — so the pawn is a blank
        /// slate a test can drive from scratch.
        /// </summary>
        private void ScrubSetupArrivalForPawn(Pawn pawn)
        {
            if (Component == null || pawn == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (string.IsNullOrEmpty(pawnId))
            {
                return;
            }

            HashSet<string> arrivalEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DiaryEventRepository repository = EventRepository();
            IReadOnlyList<DiaryEvent> allEvents = repository.AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent != null && diaryEvent.IsArrivalDescriptionFor(pawnId))
                {
                    arrivalEventIds.Add(diaryEvent.eventId);
                }
            }

            if (arrivalEventIds.Count > 0)
            {
                repository.RemoveEvents(arrivalEventIds);
                (ArchiveField?.GetValue(Component) as DiaryArchiveRepository)?.RemoveForEventIds(arrivalEventIds);

                Dictionary<string, PawnDiaryRecord> diariesById =
                    DiariesByIdField?.GetValue(Component) as Dictionary<string, PawnDiaryRecord>;
                PawnDiaryRecord record = null;
                diariesById?.TryGetValue(pawnId, out record);
                record?.eventIds?.RemoveAll(id => arrivalEventIds.Contains(id));
                DiaryStateVersion.Bump();
            }

            // The arrival hook also stamped a transient event-type dedup key embedding this pawn id; clear
            // any such recentEvents key so a test's own (same-frame) arrival submit is not swallowed as a
            // recent duplicate.
            RemoveDictionaryKeysContaining(
                RecentEventsField?.GetValue(Component) as IDictionary,
                new HashSet<string>(new[] { pawnId }, StringComparer.Ordinal));
        }

        /// <summary>
        /// Turns on Prompt Test Mode for this scope: <c>Prefs.DevMode</c> + <c>promptTestMode</c> so the
        /// generation pipeline renders and stores each event's prompt without sending it, at the given
        /// context-detail preset. All three touched settings are restored in teardown. Enable this before
        /// creating generating colonists.
        /// </summary>
        public void EnablePromptCapture(PromptContextDetailLevel level = PromptContextDetailLevel.Full)
        {
            if (PawnDiaryMod.Settings == null)
            {
                throw new AssertionException("Settings must be loaded to enable prompt capture.");
            }

            originalDevMode = Prefs.DevMode;
            originalPromptTestMode = PawnDiaryMod.Settings.promptTestMode;
            originalContextDetailLevel = PawnDiaryMod.Settings.contextDetailLevel;
            Prefs.DevMode = true;
            PawnDiaryMod.Settings.promptTestMode = true;
            PawnDiaryMod.Settings.contextDetailLevel = level;
            promptCaptureEnabled = true;
        }

        /// <summary>
        /// Reads back the prompt the runtime rendered and stored for one POV role (only meaningful under
        /// <see cref="EnablePromptCapture"/>). Asserts the role captured as prompt-only and returns the
        /// combined system+user prompt for field/template/token assertions.
        /// </summary>
        public string CapturedPrompt(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null)
            {
                throw new AssertionException("CapturedPrompt got a null diary event.");
            }

            if (PromptForMethod == null)
            {
                throw new AssertionException("Pawn Diary test harness could not locate DiaryEvent.PromptFor.");
            }

            if (StatusForMethod != null)
            {
                string status = StatusForMethod.Invoke(diaryEvent, new object[] { povRole }) as string;
                Require(
                    string.Equals(status, DiaryEvent.PromptOnlyStatus, StringComparison.Ordinal),
                    "Expected a prompt-only capture for role '" + povRole + "' but the status was '" + status + "'.");
            }

            string prompt = PromptForMethod.Invoke(diaryEvent, new object[] { povRole }) as string ?? string.Empty;
            Require(
                !string.IsNullOrWhiteSpace(prompt),
                "The captured prompt for role '" + povRole + "' was empty.");
            return prompt;
        }

        /// <summary>
        /// Registers a per-test cleanup action. Use this for state a specific test creates that the
        /// generic harness does not already restore (a spawned thing, a job, a hediff). Registered
        /// actions run first, in reverse order, and each is failure-isolated like every core step.
        /// </summary>
        public void RegisterCleanup(Action cleanup)
        {
            if (cleanup != null)
            {
                customCleanups.Add(cleanup);
            }
        }

        /// <summary>
        /// Tracks a PlayLog row the test added so teardown removes exactly it (and the audit can confirm
        /// it is gone). Vanilla PlayLog rows the test did not add are never touched.
        /// </summary>
        public void TrackPlayLogEntry(LogEntry entry)
        {
            if (entry != null)
            {
                addedPlayLogEntries.Add(entry);
            }
        }

        /// <summary>
        /// Runs <paramref name="fire"/> (a real vanilla API call the production Harmony hooks observe)
        /// and asserts it produced exactly one new <see cref="DiaryEvent"/> matching the given
        /// interaction/participants. Returns that event for further field assertions.
        /// </summary>
        public DiaryEvent FireAndRequireEvent(
            Action fire,
            string interactionDefName,
            Pawn expectedInitiator,
            Pawn expectedRecipient)
        {
            if (fire == null)
            {
                throw new AssertionException("FireAndRequireEvent needs a trigger action.");
            }

            HashSet<string> before = SnapshotEventIds();
            fire();

            string initiatorId = expectedInitiator?.GetUniqueLoadID() ?? string.Empty;
            string recipientId = expectedRecipient?.GetUniqueLoadID() ?? string.Empty;
            List<DiaryEvent> matches = new List<DiaryEvent>();
            IReadOnlyList<DiaryEvent> allEvents = EventRepository().AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent == null
                    || before.Contains(diaryEvent.eventId)
                    || !string.Equals(diaryEvent.interactionDefName, interactionDefName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(diaryEvent.initiatorPawnId, initiatorId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (expectedRecipient != null
                    && !string.Equals(diaryEvent.recipientPawnId, recipientId, StringComparison.Ordinal))
                {
                    continue;
                }

                matches.Add(diaryEvent);
            }

            if (matches.Count != 1)
            {
                throw new AssertionException(
                    "Expected exactly one new '" + interactionDefName
                    + "' diary event for the test pawn(s), but found " + matches.Count + ".");
            }

            return matches[0];
        }

        /// <summary>
        /// Runs <paramref name="fire"/> and asserts the production capture path dropped it: no new
        /// diary event that references any test pawn was created. Use this for negative-gate cases
        /// (disabled group, ineligible pawn, dedup window).
        /// </summary>
        public void RequireNoNewEvent(Action fire)
        {
            if (fire == null)
            {
                throw new AssertionException("RequireNoNewEvent needs a trigger action.");
            }

            HashSet<string> before = SnapshotEventIds();
            fire();

            HashSet<string> pawnIds = TestPawnIdSet();
            IReadOnlyList<DiaryEvent> allEvents = EventRepository().AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent == null || before.Contains(diaryEvent.eventId))
                {
                    continue;
                }

                if (pawnIds.Contains(diaryEvent.initiatorPawnId) || pawnIds.Contains(diaryEvent.recipientPawnId))
                {
                    throw new AssertionException(
                        "Expected the fired event to be dropped, but a new diary event '"
                        + diaryEvent.interactionDefName + "' was created for a test pawn.");
                }
            }
        }

        /// <summary>
        /// Asserts a two-POV event: it is not solo, its initiator/recipient ids match the two pawns,
        /// and BOTH pawns' diary indexes reference it.
        /// </summary>
        public void RequirePairRefs(DiaryEvent diaryEvent, Pawn initiator, Pawn recipient)
        {
            if (diaryEvent == null)
            {
                throw new AssertionException("RequirePairRefs got a null diary event.");
            }

            Require(!diaryEvent.solo, "Expected a pairwise diary event but it was marked solo.");
            Require(
                string.Equals(diaryEvent.initiatorPawnId, initiator?.GetUniqueLoadID(), StringComparison.Ordinal),
                "The diary event's initiator did not match the expected pawn.");
            Require(
                string.Equals(diaryEvent.recipientPawnId, recipient?.GetUniqueLoadID(), StringComparison.Ordinal),
                "The diary event's recipient did not match the expected pawn.");
            RequireDiaryRefsEvent(initiator, diaryEvent.eventId);
            RequireDiaryRefsEvent(recipient, diaryEvent.eventId);
        }

        /// <summary>
        /// Asserts a single-POV event: it is solo, its initiator id matches the pawn, it has no
        /// recipient, and the pawn's diary index references it.
        /// </summary>
        public void RequireSoloRef(DiaryEvent diaryEvent, Pawn pawn)
        {
            if (diaryEvent == null)
            {
                throw new AssertionException("RequireSoloRef got a null diary event.");
            }

            Require(diaryEvent.solo, "Expected a solo diary event but it was marked pairwise.");
            Require(
                string.Equals(diaryEvent.initiatorPawnId, pawn?.GetUniqueLoadID(), StringComparison.Ordinal),
                "The solo diary event's owner did not match the expected pawn.");
            Require(
                string.IsNullOrWhiteSpace(diaryEvent.recipientPawnId),
                "A solo diary event should not carry a recipient pawn id.");
            RequireDiaryRefsEvent(pawn, diaryEvent.eventId);
        }

        /// <summary>
        /// Restores every mutation this scope made and then audits that no test-owned state survived.
        /// Every step runs even if an earlier one throws; the first failure is re-thrown at the end so
        /// RimTest reports it, but only after all cleanup has been attempted.
        /// </summary>
        public void TearDown()
        {
            Exception firstFailure = null;

            // Per-test extras first (reverse order), while the pawns are still alive.
            for (int i = customCleanups.Count - 1; i >= 0; i--)
            {
                Action cleanup = customCleanups[i];
                TryCleanup(() => cleanup?.Invoke(), ref firstFailure);
            }

            TryCleanup(RecoverMentalStates, ref firstFailure);
            TryCleanup(ClearTestPawnRelations, ref firstFailure);
            TryCleanup(RemoveTestPlayLogEntries, ref firstFailure);
            TryCleanup(RemoveTestDiaryState, ref firstFailure);
            TryCleanup(RemoveTransientKeysForTestPawns, ref firstFailure);
            TryCleanup(RestoreGroupSettings, ref firstFailure);
            if (promptCaptureEnabled)
            {
                TryCleanup(RestorePromptCapture, ref firstFailure);
            }

            TryCleanup(DestroyTestPawns, ref firstFailure);

            // Prove the colony is clean. If a step above silently failed to remove something, this is
            // what turns "leaked state" into a visible test failure (TEST_COVERAGE_PLAN.md §9).
            TryCleanup(AuditNoLeakedState, ref firstFailure);

            // Balance the RNG push last, no matter what else failed.
            if (randPushed)
            {
                TryCleanup(() => Rand.PopState(), ref firstFailure);
                randPushed = false;
            }

            testPawns.Clear();
            testPawnIds.Clear();
            addedPlayLogEntries.Clear();
            customCleanups.Clear();
            Component = null;

            if (firstFailure != null)
            {
                throw new AssertionException(
                    "Pawn Diary test cleanup failed after attempting every cleanup step: " + firstFailure);
            }
        }

        // ----- assertion helper -------------------------------------------------------------------

        /// <summary>Throws an <see cref="AssertionException"/> with <paramref name="message"/> when false.</summary>
        public static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertionException(message);
            }
        }

        // ----- cleanup steps ----------------------------------------------------------------------

        private void RecoverMentalStates()
        {
            for (int i = 0; i < testPawns.Count; i++)
            {
                Pawn pawn = testPawns[i];
                if (pawn?.MentalState != null)
                {
                    pawn.MentalState.RecoverFromState();
                }
            }
        }

        private void ClearTestPawnRelations()
        {
            for (int i = 0; i < testPawns.Count; i++)
            {
                Pawn pawn = testPawns[i];
                if (pawn?.relations == null)
                {
                    continue;
                }

                // Copy first: RemoveDirectRelation mutates the list we would otherwise be iterating.
                List<DirectPawnRelation> relations = new List<DirectPawnRelation>(pawn.relations.DirectRelations);
                for (int r = 0; r < relations.Count; r++)
                {
                    DirectPawnRelation relation = relations[r];
                    if (relation?.def != null && relation.otherPawn != null)
                    {
                        pawn.relations.TryRemoveDirectRelation(relation.def, relation.otherPawn);
                    }
                }
            }
        }

        private void RemoveTestPlayLogEntries()
        {
            List<LogEntry> allEntries = Find.PlayLog?.AllEntries;
            if (allEntries == null || addedPlayLogEntries.Count == 0)
            {
                return;
            }

            allEntries.RemoveAll(entry => entry != null && addedPlayLogEntries.Contains(entry));
        }

        private void RemoveTestDiaryState()
        {
            if (Component == null)
            {
                return;
            }

            HashSet<string> pawnIds = TestPawnIdSet();
            HashSet<string> eventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DiaryEventRepository repository = EventRepository();
            IReadOnlyList<DiaryEvent> allEvents = repository.AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent != null
                    && (pawnIds.Contains(diaryEvent.initiatorPawnId)
                        || pawnIds.Contains(diaryEvent.recipientPawnId)))
                {
                    eventIds.Add(diaryEvent.eventId);
                }
            }

            repository.RemoveEvents(eventIds);
            DiaryArchiveRepository archive = ArchiveField?.GetValue(Component) as DiaryArchiveRepository;
            archive?.RemoveForEventIds(eventIds);

            List<PawnDiaryRecord> diaries = DiariesField?.GetValue(Component) as List<PawnDiaryRecord>;
            if (diaries != null)
            {
                for (int i = diaries.Count - 1; i >= 0; i--)
                {
                    PawnDiaryRecord diary = diaries[i];
                    if (diary?.eventIds != null)
                    {
                        diary.eventIds.RemoveAll(id => eventIds.Contains(id));
                    }

                    if (diary != null && pawnIds.Contains(diary.pawnId))
                    {
                        diaries.RemoveAt(i);
                    }
                }
            }

            Dictionary<string, PawnDiaryRecord> diariesById =
                DiariesByIdField?.GetValue(Component) as Dictionary<string, PawnDiaryRecord>;
            foreach (string pawnId in pawnIds)
            {
                diariesById?.Remove(pawnId);
            }

            DiaryStateVersion.Bump();
        }

        private void RemoveTransientKeysForTestPawns()
        {
            if (Component == null)
            {
                return;
            }

            HashSet<string> pawnIds = TestPawnIdSet();
            RemoveDictionaryKeysContaining(RecentEventsField?.GetValue(Component) as IDictionary, pawnIds);

            IDictionary commandStatus = CommandStatusField?.GetValue(Component) as IDictionary;
            foreach (string pawnId in pawnIds)
            {
                commandStatus?.Remove(pawnId);
            }

            // Pawn-scoped accumulator stores (all keyed by a string embedding the pawn id).
            RemoveDictionaryKeysContaining(PendingInteractionBatchesField?.GetValue(Component) as IDictionary, pawnIds);
            RemoveDictionaryKeysContaining(PendingAmbientNotesField?.GetValue(Component) as IDictionary, pawnIds);
            RemoveDictionaryKeysContaining(ActiveThoughtProgressionsField?.GetValue(Component) as IDictionary, pawnIds);
            RemoveDictionaryKeysContaining(PendingDayHediffsField?.GetValue(Component) as IDictionary, pawnIds);
            RemoveHashSetEntriesContaining(WrittenAmbientNotesField?.GetValue(Component) as HashSet<string>, pawnIds);
            RemoveHashSetEntriesContaining(WrittenDayReflectionsField?.GetValue(Component) as HashSet<string>, pawnIds);
        }

        private void RestorePromptCapture()
        {
            Prefs.DevMode = originalDevMode;
            if (PawnDiaryMod.Settings != null)
            {
                PawnDiaryMod.Settings.promptTestMode = originalPromptTestMode;
                PawnDiaryMod.Settings.contextDetailLevel = originalContextDetailLevel;
            }

            promptCaptureEnabled = false;
        }

        private void RestoreGroupSettings()
        {
            if (PawnDiaryMod.Settings == null || originalGroupEnabled == null)
            {
                return;
            }

            PawnDiaryMod.Settings.groupEnabled.Clear();
            foreach (KeyValuePair<string, bool> pair in originalGroupEnabled)
            {
                PawnDiaryMod.Settings.groupEnabled[pair.Key] = pair.Value;
            }

            originalGroupEnabled = null;
        }

        private void DestroyTestPawns()
        {
            for (int i = 0; i < testPawns.Count; i++)
            {
                Pawn pawn = testPawns[i];
                if (pawn == null || pawn.Destroyed)
                {
                    continue;
                }

                // A prompt-capture suite may have spawned this pawn (see SpawnAsLiveColonist), or the death
                // suite may have left it in WorldPawns. Drop any world-pawn entry first so nothing leaks
                // into the world; Destroy(Vanish) then despawns a spawned pawn cleanly, with no corpse,
                // notification, or letter.
                if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.RemovePawn(pawn);
                }

                pawn.Destroy(DestroyMode.Vanish);
            }
        }

        // ----- no-leak audit ----------------------------------------------------------------------

        // After every removal step, confirm the loaded colony holds no trace of this test: no saved
        // event or diary index referencing a test pawn, no tracked Social-log row, and no transient
        // dedup/command key. This is the machine check behind the plan's "zero marked state" gate.
        private void AuditNoLeakedState()
        {
            if (Component == null)
            {
                return;
            }

            HashSet<string> pawnIds = TestPawnIdSet();

            IReadOnlyList<DiaryEvent> allEvents = EventRepository().AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent != null
                    && (pawnIds.Contains(diaryEvent.initiatorPawnId)
                        || pawnIds.Contains(diaryEvent.recipientPawnId)))
                {
                    throw new AssertionException(
                        "Leak audit: a diary event referencing a test pawn survived cleanup.");
                }
            }

            Dictionary<string, PawnDiaryRecord> diariesById =
                DiariesByIdField?.GetValue(Component) as Dictionary<string, PawnDiaryRecord>;
            if (diariesById != null)
            {
                foreach (string pawnId in pawnIds)
                {
                    if (diariesById.ContainsKey(pawnId))
                    {
                        throw new AssertionException(
                            "Leak audit: a diary index for a test pawn survived cleanup.");
                    }
                }
            }

            List<LogEntry> allEntries = Find.PlayLog?.AllEntries;
            if (allEntries != null)
            {
                for (int i = 0; i < allEntries.Count; i++)
                {
                    if (allEntries[i] != null && addedPlayLogEntries.Contains(allEntries[i]))
                    {
                        throw new AssertionException(
                            "Leak audit: a test Social-log row survived cleanup.");
                    }
                }
            }

            if (DictionaryHasKeyContaining(RecentEventsField?.GetValue(Component) as IDictionary, pawnIds)
                || DictionaryHasKeyContaining(CommandStatusField?.GetValue(Component) as IDictionary, pawnIds))
            {
                throw new AssertionException(
                    "Leak audit: a transient dedup/command key for a test pawn survived cleanup.");
            }

            if (DictionaryHasKeyContaining(PendingInteractionBatchesField?.GetValue(Component) as IDictionary, pawnIds)
                || DictionaryHasKeyContaining(PendingAmbientNotesField?.GetValue(Component) as IDictionary, pawnIds)
                || DictionaryHasKeyContaining(ActiveThoughtProgressionsField?.GetValue(Component) as IDictionary, pawnIds)
                || DictionaryHasKeyContaining(PendingDayHediffsField?.GetValue(Component) as IDictionary, pawnIds)
                || HashSetHasEntryContaining(WrittenAmbientNotesField?.GetValue(Component) as HashSet<string>, pawnIds)
                || HashSetHasEntryContaining(WrittenDayReflectionsField?.GetValue(Component) as HashSet<string>, pawnIds))
            {
                throw new AssertionException(
                    "Leak audit: a pending interaction-batch / thought-progression / day-hediff / day-reflection key for a test pawn survived cleanup.");
            }
        }

        // ----- shared internals -------------------------------------------------------------------

        private HashSet<string> SnapshotEventIds()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<DiaryEvent> allEvents = EventRepository().AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent != null && !string.IsNullOrWhiteSpace(diaryEvent.eventId))
                {
                    ids.Add(diaryEvent.eventId);
                }
            }

            return ids;
        }

        private void RequireDiaryRefsEvent(Pawn pawn, string eventId)
        {
            string pawnId = pawn?.GetUniqueLoadID();
            Dictionary<string, PawnDiaryRecord> diariesById =
                DiariesByIdField?.GetValue(Component) as Dictionary<string, PawnDiaryRecord>;
            PawnDiaryRecord diary = null;
            if (!string.IsNullOrEmpty(pawnId))
            {
                diariesById?.TryGetValue(pawnId, out diary);
            }

            Require(
                diary?.eventIds != null && diary.eventIds.Contains(eventId),
                "The pawn's diary index did not reference the new event.");
        }

        private HashSet<string> TestPawnIdSet()
        {
            return new HashSet<string>(testPawnIds, StringComparer.Ordinal);
        }

        private DiaryEventRepository EventRepository()
        {
            DiaryEventRepository repository = EventsField?.GetValue(Component) as DiaryEventRepository;
            if (repository == null)
            {
                throw new AssertionException(
                    "Could not read Pawn Diary's event repository for the integration assertion.");
            }

            return repository;
        }

        private static void RemoveDictionaryKeysContaining(IDictionary dictionary, HashSet<string> pawnIds)
        {
            if (dictionary == null || dictionary.Count == 0)
            {
                return;
            }

            List<object> remove = new List<object>();
            foreach (object key in dictionary.Keys)
            {
                if (KeyContainsAnyPawnId(key as string, pawnIds))
                {
                    remove.Add(key);
                }
            }

            for (int i = 0; i < remove.Count; i++)
            {
                dictionary.Remove(remove[i]);
            }
        }

        private static bool DictionaryHasKeyContaining(IDictionary dictionary, HashSet<string> pawnIds)
        {
            if (dictionary == null || dictionary.Count == 0)
            {
                return false;
            }

            foreach (object key in dictionary.Keys)
            {
                if (KeyContainsAnyPawnId(key as string, pawnIds))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveHashSetEntriesContaining(HashSet<string> set, HashSet<string> pawnIds)
        {
            set?.RemoveWhere(entry => KeyContainsAnyPawnId(entry, pawnIds));
        }

        private static bool HashSetHasEntryContaining(HashSet<string> set, HashSet<string> pawnIds)
        {
            if (set == null || set.Count == 0)
            {
                return false;
            }

            foreach (string entry in set)
            {
                if (KeyContainsAnyPawnId(entry, pawnIds))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool KeyContainsAnyPawnId(string key, HashSet<string> pawnIds)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            foreach (string pawnId in pawnIds)
            {
                if (key.IndexOf(pawnId, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RequireReflectionField(FieldInfo field, string fieldName)
        {
            if (field == null)
            {
                throw new AssertionException(
                    "Pawn Diary test harness could not locate private field '" + fieldName + "'.");
            }
        }

        // Cleanup must keep going after one failure so a broken assertion never strands a test pawn,
        // relation, Social-log row, or saved diary reference in the developer's loaded colony.
        private static void TryCleanup(Action action, ref Exception firstFailure)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception exception)
            {
                if (firstFailure == null)
                {
                    firstFailure = exception;
                }
            }
        }
    }
}

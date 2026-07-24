// Prompt-capture fixture for Pawn Diary's §4.2 reflection templates: SoloDayReflection,
// SoloQuadrumReflection, SoloArcReflection, and SoloBeliefReflection
// (design/TEST_COVERAGE_PLAN.md §4.2).
//
// With Prompt Test Mode enabled (harness EnablePromptCapture) a fired reflection on a generating
// colonist runs the real resolver: it picks the reflection template, renders the system + user prompt,
// stamps it on the event, and STOPS before any network call. Every reflection is a SOLO event queued on
// the initiator role, so it captures synchronously the instant it is submitted.
//
// The reflections are normally produced by DiaryGameComponent.ArbitrateReflectionsForPawn during the
// sleep-path scan, using the source-specific PrepareDay/Quadrum/ArcReflectionCandidate helpers.
// Reproducing that scan needs the storyteller clock, a bedded-down
// colonist, and the pawn's whole event graph, so — following the EVT-19/EVT-20 pattern in
// PawnDiaryDayReflectionFlowTests / PawnDiaryArcReflectionFlowTests — these tests build the exact
// reflection signal each flush would Dispatch and submit it directly through DiaryEvents.Submit,
// controlling the game context so each template's marker fields render deterministically. Day/quadrum
// share the DayReflectionSignal + DayReflectionEventData carrier (the template is chosen from the
// gameContext's day_reflection / quadrum_reflection markers, not the DefName); arc uses ArcReflectionSignal.
//
// Which template resolves, the per-template token cap, and direct-speech instruction omission are
// asserted through the pure DiaryPipelineAdapters.BuildPromptRequest + DiaryPromptPlanner path (the
// same policy the live QueuePrompt consumes). A distinctive injected instruction proves the template
// drops that channel without confusing legitimate writing-style rules that mention the [[speech]]
// marker while forbidding it. Rendered event/context fields are still asserted on the captured prompt.
//
// Determinism: RNG is isolated by the harness; every asserted gameContext value is passed in literally.
// The two reflection master switches (daySummaryEnabled gates the day/quadrum signal, arcReflectionEnabled
// gates the arc signal) live on the shared DiaryTuning def, which the harness does NOT auto-restore, so
// they are snapshotted and restored in failure-safe cleanup.
//
// Coverage area (design/TEST_COVERAGE_PLAN.md §4.2): reflection prompt templates.
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
    /// Proves the §4.2 reflection templates each resolve through the live prompt-capture pipeline:
    /// SoloDayReflection/SoloQuadrumReflection/SoloArcReflection/SoloBeliefReflection render their marker
    /// fields, omit the direct-speech instruction, drive the intended token cap (quadrum 350, arc 420,
    /// belief 360, day none), and that only the day template renders an 'important context' field. Also
    /// pins the group/color routing a belief page depends on. Requires a loaded game because the capture
    /// pipeline (like every capture path) is inert at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryReflectionTemplateFixtureTests
    {
        private const int ArcYear = 5500;

        // A distinctive quadrum date-range string so the rendered "quadrum dates:" line is unambiguous.
        private const string QuadrumDates = "Aprimay 1 - Aprimay 5";

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        // Snapshot of the two reflection master switches this suite forces on (restored in cleanup).
        private static DiaryTuningDef tuning;
        private static bool savedDaySummaryEnabled;
        private static bool savedArcReflectionEnabled;

        /// <summary>
        /// Opens a scope with the Reflection group enabled (defName <c>reflection</c>, which claims
        /// DayReflection / QuadrumReflection / PawnArcReflection via matchDefNames), turns on prompt
        /// capture at the Full preset BEFORE creating the generating colonist (so even the founding-arrival
        /// hook is captured prompt-only and can never send a request), and forces the day-summary and
        /// arc-reflection signal switches on so the emission gates are not random.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            // Each reflection kind now owns its own Reflection-domain row (day/quadrum moved out of
            // the Interaction domain, belief split off for the Ideology color), and every signal gates
            // userEnabled on the row that claims its page — so all four have to be on here.
            scope = PawnDiaryRimTestScope.Begin(
                "reflection", "dayreflection", "quadrumreflection", "reflectionBelief");
            scope.EnablePromptCapture(PromptContextDetailLevel.Full);
            pawn = scope.CreateGeneratingAdultColonist();

            tuning = DiaryTuning.Current;
            savedDaySummaryEnabled = tuning.daySummaryEnabled;
            savedArcReflectionEnabled = tuning.arcReflectionEnabled;
            // DayReflectionSignal.BuildContext gates on daySummaryEnabled for both the day and quadrum
            // carriers; ArcReflectionSignal.BuildContext gates on arcReflectionEnabled.
            tuning.daySummaryEnabled = true;
            tuning.arcReflectionEnabled = true;
            scope.RegisterCleanup(RestoreReflectionTuning);
        }

        /// <summary>
        /// Restores DevMode/promptTestMode/contextDetailLevel (via the harness) and the two forced tuning
        /// switches (via the registered cleanup), then audits for leaks — even when a test threw partway.
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
            }
        }

        /// <summary>
        /// §4.2 SoloDayReflection. A submitted day reflection resolves the day-reflection template and
        /// renders its controlled body with no direct-speech instruction. The day template drives no
        /// per-template token cap (0 => the player's normal setting) and, unlike quadrum/arc, DOES define
        /// an 'important context' field.
        /// </summary>
        [Test]
        public static void DayReflectionResolvesTemplateOmitsDirectSpeechAndDrivesNoTokenCap()
        {
            const string povText = "Turning the day over before sleep.";
            const string instruction = "Reflect on the day just past.";
            string gameContext = DayReflectionEventData.BuildGameContext(
                day: 42, highlightCount: 2, candidateCount: 4, fillerMomentCount: 0, signalTags: "hediff:TestFlu");
            DayReflectionEventData data = new DayReflectionEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = DayReflectionEventData.DefNameToken,
                Day = 42,
                CandidateCount = 4,
                ImportantCandidateCount = 4,
                HighlightCount = 2,
                FillerMomentCount = 0,
                SignalTags = "hediff:TestFlu",
                AlreadyWritten = false,
            };

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new DayReflectionSignal(
                    data, pawn, "Day reflection", povText, instruction, gameContext)),
                DayReflectionEventData.DefNameToken,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            string prompt = scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole);

            // Reflection marker: the live resolver selects the day-reflection template and renders the
            // controlled reflection instruction line through it.
            RequireTemplate(diaryEvent, DiaryPromptTemplates.SoloDayReflection);
            RequireContains(prompt, "instruction: " + instruction, "day reflection instruction line");

            // Direct-speech instruction is omitted for every reflection template.
            RequireDirectSpeechOmitted(diaryEvent, DiaryPromptTemplates.SoloDayReflection);

            // The day template drives no per-template cap (0), unlike the quadrum/arc templates.
            RequireTokenCap(diaryEvent, DiaryPromptTemplates.SoloDayReflection, 0);

            // Contrast: the day template DOES define an 'important context' (PromptEnchantment) field.
            PawnDiaryRimTestScope.Require(
                TemplateHasPromptEnchantmentField(DiaryPromptTemplates.SoloDayReflection),
                "SoloDayReflection should define an 'important context' (PromptEnchantment) field.");
        }

        /// <summary>
        /// §4.2 SoloQuadrumReflection. A submitted quadrum reflection resolves the quadrum template and
        /// renders its marker fields (quadrum dates + important entry count), omits direct speech, drives
        /// the 350-token cap, and does NOT render an 'important context' field.
        /// </summary>
        [Test]
        public static void QuadrumReflectionRendersMarkerFieldsDrives350CapAndOmitsImportantContext()
        {
            const string povText = "The season, read back to back.";
            const string instruction = "Look back across the whole quadrum.";
            string gameContext = DayReflectionEventData.BuildQuadrumGameContext(
                day: 42, quadrum: 1, quadrumStartDay: 15, quadrumEndDay: 29, quadrumDates: QuadrumDates,
                dueDay: 28, highlightCount: 2, candidateCount: 4, signalTags: "event:Raid");
            DayReflectionEventData data = new DayReflectionEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = DayReflectionEventData.QuadrumDefNameToken,
                Day = 42,
                CandidateCount = 4,
                ImportantCandidateCount = 4,
                HighlightCount = 2,
                FillerMomentCount = 0,
                SignalTags = "event:Raid",
                AlreadyWritten = false,
            };

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new DayReflectionSignal(
                    data, pawn, "Quadrum reflection", povText, instruction, gameContext)),
                DayReflectionEventData.QuadrumDefNameToken,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            string prompt = scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole);

            // The template resolves to the quadrum shape, and its marker context fields render.
            RequireTemplate(diaryEvent, DiaryPromptTemplates.SoloQuadrumReflection);
            RequireContains(prompt, "quadrum dates: " + QuadrumDates, "quadrum dates marker field");
            RequireContains(prompt, "important entry count: 4", "quadrum important-entry-count marker field");

            RequireDirectSpeechOmitted(diaryEvent, DiaryPromptTemplates.SoloQuadrumReflection);

            // The quadrum template drives the 350-token cap.
            RequireTokenCap(diaryEvent, DiaryPromptTemplates.SoloQuadrumReflection, 350);

            // Intentional absence: the quadrum template has no 'important context' (PromptEnchantment)
            // field, so that line never renders and the field list never contains it.
            RequireImportantContextFieldAbsent(prompt, DiaryPromptTemplates.SoloQuadrumReflection);
        }

        /// <summary>
        /// §4.2 SoloArcReflection. A submitted arc reflection resolves the arc template and renders its
        /// marker fields (arc year + selected/candidate memory counts), omits direct speech, drives the
        /// 420-token cap, and does NOT render an 'important context' field.
        /// </summary>
        [Test]
        public static void ArcReflectionRendersMarkerFieldsDrives420CapAndOmitsImportantContext()
        {
            const string povText = "Raised a granary wall. Held the line against a raider.";
            const string instruction = "Look back over the year.";
            string gameContext = ArcReflectionEventData.BuildGameContext(
                ArcYear, forced: false, selectedMemories: 2, candidateMemories: 2, entriesThisYear: 0);
            ArcReflectionEventData data = new ArcReflectionEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = ArcReflectionEventData.DefNameToken,
                ArcYear = ArcYear,
                CandidateMemoryCount = 2,
                SelectedMemoryCount = 2,
                EntriesThisYear = 0,
                Forced = false,
                AlreadyWritten = false,
            };

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new ArcReflectionSignal(
                    data, pawn, "the year in review", povText, instruction, gameContext)),
                ArcReflectionEventData.DefNameToken,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            string prompt = scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole);

            // The template resolves to the arc shape, and its marker context fields render.
            RequireTemplate(diaryEvent, DiaryPromptTemplates.SoloArcReflection);
            RequireContains(prompt, "arc year: " + ArcYear, "arc-year marker field");
            RequireContains(prompt, "selected memory count: 2", "arc selected-memory-count marker field");
            RequireContains(prompt, "candidate memory count: 2", "arc candidate-memory-count marker field");

            RequireDirectSpeechOmitted(diaryEvent, DiaryPromptTemplates.SoloArcReflection);

            // The arc template drives the 420-token cap.
            RequireTokenCap(diaryEvent, DiaryPromptTemplates.SoloArcReflection, 420);

            // Intentional absence: the arc template has no 'important context' (PromptEnchantment) field.
            RequireImportantContextFieldAbsent(prompt, DiaryPromptTemplates.SoloArcReflection);
        }

        /// <summary>
        /// §4.2 SoloBeliefReflection. An Ideology belief reflection resolves the belief template, drives
        /// the policy's own token cap, and omits direct speech like every other reflection. Skips cleanly
        /// without Ideology, where the signal is gated off and no page can exist.
        /// </summary>
        [Test]
        public static void BeliefReflectionResolvesItsTemplateAndDrivesThePolicyTokenCap()
        {
            if (!ModsConfig.IdeologyActive)
            {
                // Not a silent pass: prove the gate is what stops it, so a broken gate cannot hide here.
                // BeliefReflectionSignal.BuildContext reports signalEnabled=false without Ideology, so
                // the catalog drops the payload and no page may be created.
                scope.RequireNoNewEvent(
                    () => DiaryEvents.Submit(BeliefSignalFor("Nothing has settled yet.")));
                return;
            }

            const string povText = "Turning the ideoligion over in the dark.";
            const string instruction = "Reflect on what you still believe.";
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(BeliefSignalFor(povText, instruction)),
                BeliefReflectionEventData.DefNameToken,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            string prompt = scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole);

            RequireTemplate(diaryEvent, DiaryPromptTemplates.SoloBeliefReflection);
            RequireContains(prompt, "instruction: " + instruction, "belief reflection instruction line");
            RequireDirectSpeechOmitted(diaryEvent, DiaryPromptTemplates.SoloBeliefReflection);

            // The belief template drives a 360-token cap of its own, like quadrum's 350 and arc's 420.
            RequireTokenCap(diaryEvent, DiaryPromptTemplates.SoloBeliefReflection, 360);
        }

        /// <summary>
        /// The routing this suite's belief page depends on, asserted directly against the loaded catalog:
        /// the saved <c>belief_reflection=</c> marker recovers the Reflection domain, that domain's
        /// <c>reflectionBelief</c> row claims the page, and the page is stamped with the Ideology core cue
        /// and marked important. The marker had no case in DomainForContext, so belief pages recovered as
        /// the Interaction social catch-all: unimportant, toneless, and washed out to the "quiet" cue.
        ///
        /// Deliberately DLC-independent — group classification is not availability-filtered, so an old
        /// save written with Ideology must keep its color when it is loaded without the DLC.
        /// </summary>
        [Test]
        public static void BeliefReflectionRecoversItsIdeologyGroupFromSavedContext()
        {
            string savedContext = BeliefReflectionEventData.BuildGameContext(
                BeliefReflectionTriggerTokens.IdeologyChange);

            PawnDiaryRimTestScope.Require(
                string.Equals(
                    DiaryEventDomainClassifier.DomainForContext(savedContext),
                    DiaryEventDomainClassifier.Reflection,
                    StringComparison.Ordinal),
                "A saved belief reflection did not recover the Reflection domain, so display and prompt "
                + "policy fall back to the Interaction social catch-all.");

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyDefName(
                GroupDomain.Reflection, BeliefReflectionEventData.DefNameToken);
            PawnDiaryRimTestScope.Require(
                group != null && string.Equals(group.defName, "reflectionBelief", StringComparison.Ordinal),
                "The belief reflection resolved group '" + (group?.defName ?? "<null>")
                + "' instead of the dedicated reflectionBelief row.");
            PawnDiaryRimTestScope.Require(group.important,
                "The belief reflection group is not marked important, so its pages lose the UI marker.");

            PawnDiaryRimTestScope.Require(
                string.Equals(
                    DiaryEvent.ResolveColorCue(BeliefReflectionEventData.DefNameToken, savedContext),
                    DiaryEvent.IdeologyColorCue,
                    StringComparison.Ordinal),
                "A belief reflection page did not stamp the Ideology core color cue.");

            // The split must not have stolen the other three reflections from the generic row.
            string[] genericNames =
            {
                DayReflectionEventData.DefNameToken,
                DayReflectionEventData.QuadrumDefNameToken,
                ArcReflectionEventData.DefNameToken
            };
            for (int i = 0; i < genericNames.Length; i++)
            {
                DiaryInteractionGroupDef generic = InteractionGroups.ClassifyDefName(
                    GroupDomain.Reflection, genericNames[i]);
                PawnDiaryRimTestScope.Require(
                    generic != null && string.Equals(generic.defName, "reflection", StringComparison.Ordinal),
                    "'" + genericNames[i] + "' no longer classifies to the generic reflection row (got '"
                    + (generic?.defName ?? "<null>") + "').");
            }

            // The split exists only to add a color: the wording handed to the model must be unchanged.
            DiaryInteractionGroupDef genericRow = InteractionGroups.ByKey("reflection");
            PawnDiaryRimTestScope.Require(genericRow != null, "The generic reflection group is missing.");
            PawnDiaryRimTestScope.Require(
                string.Equals(group.instruction, genericRow.instruction, StringComparison.Ordinal)
                    && string.Equals(group.tone, genericRow.tone, StringComparison.Ordinal),
                "The belief reflection group's localized prompt wording drifted from the generic "
                + "reflection row, so a belief page no longer reads like the reflection it copies.");
        }

        // Builds the exact signal DispatchPreparedBeliefReflection sends, with a literal frozen belief
        // block so nothing depends on the live pawn's ideo tracker or on saved reflection debt.
        private static BeliefReflectionSignal BeliefSignalFor(string povText, string instruction = "Reflect.")
        {
            BeliefReflectionEventData data = new BeliefReflectionEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = BeliefReflectionEventData.DefNameToken,
                Trigger = BeliefReflectionTriggerTokens.IdeologyChange,
                HasBeliefContext = true,
                AlreadyWritten = false,
            };
            BeliefContextBuildResult prepared = new BeliefContextBuildResult
            {
                evaluated = true,
                fullContext = "faith: steady",
                resolution = new BeliefStanceResolution()
            };
            return new BeliefReflectionSignal(
                data,
                pawn,
                "Belief reflection",
                povText,
                instruction,
                BeliefReflectionEventData.BuildGameContext(BeliefReflectionTriggerTokens.IdeologyChange),
                prepared);
        }

        // ----- template/policy assertions (pure pipeline path) ------------------------------------

        // Builds the same prompt request the live QueuePrompt resolves for this event's initiator POV.
        // maxTokens is passed as 0 so the resolved template's own cap is the one that surfaces.
        private static DiaryPromptRequest RequestFor(DiaryEvent diaryEvent)
        {
            return DiaryPipelineAdapters.BuildPromptRequest(
                diaryEvent,
                DiaryEvent.InitiatorRole,
                personaRule: null,
                psychotypeRule: null,
                promptEnchantment: null,
                humorCue: null,
                priorInitiatorEntry: null,
                entryText: null,
                titleRequest: false,
                maxTokens: 0);
        }

        // Asserts the live template selector routes this event to the expected reflection template shape.
        private static void RequireTemplate(DiaryEvent diaryEvent, string expectedTemplateKey)
        {
            string templateKey = DiaryPromptPlanner.TemplateKeyFor(RequestFor(diaryEvent));
            PawnDiaryRimTestScope.Require(
                string.Equals(templateKey, expectedTemplateKey, StringComparison.OrdinalIgnoreCase),
                "Expected the event to resolve the '" + expectedTemplateKey + "' template, but got '" + templateKey + "'.");
        }

        // Asserts the resolved template policy drives the expected per-template response cap (0 => the
        // player's normal maxTokens; a positive value is the template's own cap).
        private static void RequireTokenCap(DiaryEvent diaryEvent, string templateKey, int expectedCap)
        {
            DiaryTemplatePolicy template = RequestFor(diaryEvent).policy.Template(templateKey);
            PawnDiaryRimTestScope.Require(
                template.maxTokens == expectedCap,
                "Expected the '" + templateKey + "' template to drive a " + expectedCap
                + "-token cap, but its policy carried " + template.maxTokens + ".");
        }

        // Returns true when the template's rendered field list contains an enabled PromptEnchantment
        // (the 'important context') field.
        private static bool TemplateHasPromptEnchantmentField(string templateKey)
        {
            List<DiaryPromptFieldDef> fields = DiaryPromptTemplates.FieldsFor(templateKey);
            if (fields == null)
            {
                return false;
            }

            for (int i = 0; i < fields.Count; i++)
            {
                DiaryPromptFieldDef field = fields[i];
                if (field != null
                    && field.enabled
                    && string.Equals(field.source, "PromptEnchantment", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // ----- captured-prompt string assertions --------------------------------------------------

        private static void RequireContains(string prompt, string expected, string what)
        {
            PawnDiaryRimTestScope.Require(
                prompt != null && prompt.IndexOf(expected, StringComparison.Ordinal) >= 0,
                "The captured reflection prompt did not contain the expected " + what + " ('" + expected + "').");
        }

        // Voice/persona rules may legitimately mention the stable marker while telling a mute pawn never
        // to use it, so scanning the whole prompt for "[[speech]]" produces a false failure. Assert the
        // actual channel instead: the non-interaction adapter supplies no instruction, the loaded template
        // disables appending one, and even an injected sentinel cannot reach the rendered user prompt.
        private static void RequireDirectSpeechOmitted(DiaryEvent diaryEvent, string templateKey)
        {
            const string instructionSentinel = "PDTEST_DIRECT_SPEECH_INSTRUCTION_73f4";
            DiaryPromptRequest request = RequestFor(diaryEvent);
            PawnDiaryRimTestScope.Require(
                string.IsNullOrWhiteSpace(request.directSpeechInstruction),
                "The reflection adapter unexpectedly supplied a direct-speech instruction.");
            DiaryTemplatePolicy template = request.policy.Template(templateKey);
            PawnDiaryRimTestScope.Require(
                template != null && !template.appendDirectSpeechInstruction,
                "The '" + templateKey + "' template unexpectedly enables the direct-speech instruction channel.");

            request.directSpeechInstruction = instructionSentinel;
            DiaryPromptPlan rendered = DiaryPromptPlanner.Build(request);
            PawnDiaryRimTestScope.Require(
                rendered?.userPrompt != null
                    && rendered.userPrompt.IndexOf(instructionSentinel, StringComparison.Ordinal) < 0,
                "The '" + templateKey + "' template appended a direct-speech instruction sentinel.");
        }

        // Asserts the intentional current absence of the 'important context' field for quadrum/arc: the
        // rendered label is nowhere in the captured prompt AND the template's field list omits it.
        private static void RequireImportantContextFieldAbsent(string prompt, string templateKey)
        {
            PawnDiaryRimTestScope.Require(
                prompt != null && prompt.IndexOf("important context", StringComparison.OrdinalIgnoreCase) < 0,
                "The " + templateKey + " prompt unexpectedly rendered an 'important context' field.");
            PawnDiaryRimTestScope.Require(
                !TemplateHasPromptEnchantmentField(templateKey),
                "The " + templateKey + " template unexpectedly defines an 'important context' (PromptEnchantment) field.");
        }

        // ----- cleanup ----------------------------------------------------------------------------

        private static void RestoreReflectionTuning()
        {
            if (tuning == null)
            {
                return;
            }

            tuning.daySummaryEnabled = savedDaySummaryEnabled;
            tuning.arcReflectionEnabled = savedArcReflectionEnabled;
        }
    }
}

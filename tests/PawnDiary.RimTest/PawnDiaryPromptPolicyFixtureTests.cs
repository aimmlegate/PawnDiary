// Prompt-policy / key-resolution fixture for Pawn Diary (design/TEST_COVERAGE_PLAN.md §4.1: domain/policy +
// key resolution). §4.1 is about which XML prompt policy a captured event resolves to — NOT how the
// final prose reads. The resolver tries an ORDERED candidate list and uses the first key that has a
// loaded DiaryEventPromptDef:
//
//     exact source defName  ->  matched interaction-group defName  ->  domain classifier key  ->  broad domain fallback
//
// (see DiaryEventPromptKeys.CandidateKeys + DiaryEventPrompts.ForFirstAvailableKey, both consumed by
// DiaryPipelineAdapters.PolicyFor). This fixture makes that precedence observable.
//
// APPROACH (reported in the schema output): the resolved key is a stable internal token
// ("MentalState", "Interaction") that never appears verbatim in a rendered prompt string, and the only
// key-specific TEXT that does reach the prompt is the translatable DiaryEventPromptDef.prompt. So
// asserting the resolved key from a captured prompt string is impractical and language-fragile.
// Instead — exactly as the assignment permits — the precedence tests call the internal
// DiaryPipelineAdapters.BuildPromptRequest / the pure key resolver directly on constructed events and
// assert the ordered candidate keys + the CHOSEN policy (request.policy.group.eventPromptKey / domain /
// classifierKey). One additional test fires a real vanilla mental-state event through the harness and
// captures the prompt-only prompt, proving the live capture pipeline reaches the same resolved policy.
//
// Only shipped core Defs are relied on (DiaryEventPromptDefs.xml ships one Def per domain: "Interaction",
// "MentalState", "Tale", ...; no core Def is keyed to a specific source defName or interaction-group
// defName). That is why the (a)/(b) precedence tests drive the resolver with domain keys as the
// specific-vs-broad candidates: it is the same first-available-key mechanism the pipeline uses.
using System;
using System.Collections.Generic;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves Pawn Diary's event -> prompt-policy key resolution: the ordered candidate list
    /// (defName -> group -> classifier -> domain), first-available-key selection, and the safe
    /// domain fallback that an unknown/modded source lands on.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryPromptPolicyFixtureTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        /// <summary>
        /// Enables prompt capture (DevMode + promptTestMode, restored in teardown) BEFORE creating a
        /// generation-enabled colonist, so the one live-fire test can capture a real prompt without any
        /// network call. The violent-mental-break group is enabled for that fired Berserk event.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("mentalbreakViolent");
            scope.EnablePromptCapture();
            pawn = scope.CreateGeneratingAdultColonist();
        }

        /// <summary>
        /// Restores every mutation (settings + prompt-capture snapshot) and audits that no test-owned
        /// event, diary, or transient key survived, even after a mid-test failure.
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
        /// §4.1 candidate-key contract. DiaryEventPromptKeys.CandidateKeys emits the four resolution
        /// keys in strict precedence order — exact source defName, matched group defName, domain
        /// classifier key, broad domain fallback — and collapses duplicates (the very common case where
        /// the classifier key equals the source defName). This is the pure ordering the whole pipeline
        /// resolves against.
        /// </summary>
        [Test]
        public static void CandidateKeysAreOrderedDefNameGroupClassifierThenDomainFallback()
        {
            DiaryEventPayload payload = new DiaryEventPayload { defName = "SourceDefName" };

            List<string> ordered = DiaryEventPromptKeys.CandidateKeys(
                payload, "GroupDefName", "ClassifierKey", "DomainFallback");

            PawnDiaryRimTestScope.Require(
                ordered != null && ordered.Count == 4,
                "Expected four distinct candidate keys but got " + (ordered == null ? "null" : ordered.Count.ToString()) + ".");
            PawnDiaryRimTestScope.Require(
                string.Equals(ordered[0], "SourceDefName", StringComparison.Ordinal),
                "The exact source defName must be the highest-precedence candidate key.");
            PawnDiaryRimTestScope.Require(
                string.Equals(ordered[1], "GroupDefName", StringComparison.Ordinal),
                "The matched interaction-group defName must come after the source defName.");
            PawnDiaryRimTestScope.Require(
                string.Equals(ordered[2], "ClassifierKey", StringComparison.Ordinal),
                "The domain classifier key must come after the group defName.");
            PawnDiaryRimTestScope.Require(
                string.Equals(ordered[3], "DomainFallback", StringComparison.Ordinal),
                "The broad domain key must be the last-resort candidate key.");

            // Dedup: for an ordinary event the classifier key IS the source defName, so the list must
            // collapse to [defName, domainFallback] and never try the same key twice.
            List<string> deduped = DiaryEventPromptKeys.CandidateKeys(
                payload, "SourceDefName", "SourceDefName", DiaryEventDomainClassifier.Interaction);

            PawnDiaryRimTestScope.Require(
                deduped != null && deduped.Count == 2,
                "Repeated candidate keys must collapse to a distinct list; got "
                + (deduped == null ? "null" : deduped.Count.ToString()) + " entries.");
            PawnDiaryRimTestScope.Require(
                string.Equals(deduped[0], "SourceDefName", StringComparison.Ordinal)
                    && string.Equals(deduped[1], DiaryEventDomainClassifier.Interaction, StringComparison.Ordinal),
                "Dedup must keep first-seen order: source defName then the broad domain fallback.");
        }

        /// <summary>
        /// §4.1 case (a): when a higher-precedence candidate key has its own loaded DiaryEventPromptDef,
        /// resolution stops there and uses it instead of the broader domain key. Driven through the exact
        /// resolver the pipeline uses (DiaryEventPrompts.ForFirstAvailableKey) with two shipped domain
        /// Defs standing in for the specific-vs-broad candidates.
        /// </summary>
        [Test]
        public static void ExactHigherPrecedenceKeyWithLoadedDefWins()
        {
            string resolvedKey;
            DiaryEventPromptDef def = DiaryEventPrompts.ForFirstAvailableKey(
                new List<string> { DiaryEventDomainClassifier.Tale, DiaryEventDomainClassifier.Interaction },
                out resolvedKey);

            PawnDiaryRimTestScope.Require(
                def != null,
                "The shipped Tale event-prompt Def should resolve for the 'Tale' key.");
            PawnDiaryRimTestScope.Require(
                string.Equals(resolvedKey, DiaryEventDomainClassifier.Tale, StringComparison.Ordinal),
                "The higher-precedence loaded key ('Tale') must win over the broader 'Interaction' key, but resolved '" + resolvedKey + "'.");
            PawnDiaryRimTestScope.Require(
                string.Equals(def.eventType, DiaryEventDomainClassifier.Tale, StringComparison.OrdinalIgnoreCase),
                "The resolved Def must be the Tale policy, not the Interaction fallback.");
        }

        /// <summary>
        /// §4.1 case (b): when the higher-precedence candidate key has NO loaded Def (an unknown source
        /// defName or an unclaimed group), resolution falls THROUGH it to the next candidate that does —
        /// here the broader domain key. This is the "only a broader match exists" path.
        /// </summary>
        [Test]
        public static void UnmatchedSpecificKeyFallsThroughToBroaderLoadedKey()
        {
            string resolvedKey;
            DiaryEventPromptDef def = DiaryEventPrompts.ForFirstAvailableKey(
                new List<string> { "PawnDiaryTest_UnmatchedSourceDefName", DiaryEventDomainClassifier.Interaction },
                out resolvedKey);

            PawnDiaryRimTestScope.Require(
                def != null,
                "Resolution should still land on the loaded Interaction Def.");
            PawnDiaryRimTestScope.Require(
                string.Equals(resolvedKey, DiaryEventDomainClassifier.Interaction, StringComparison.Ordinal),
                "An unmatched specific key must be skipped so the broader 'Interaction' key resolves, but got '" + resolvedKey + "'.");
        }

        /// <summary>
        /// §4.1 case (c): a real mental-state event whose specific defName ("Berserk") and matched group
        /// ("mentalbreakViolent") have no DiaryEventPromptDef of their own resolves to the SAFE domain
        /// fallback "MentalState". Uses the internal BuildPromptRequest end to end and asserts the chosen
        /// policy — domain, classifier, and the resolved event-prompt key.
        /// </summary>
        [Test]
        public static void MentalStateEventResolvesToSafeMentalStateDomainFallback()
        {
            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = "pawndiary-test-mentalstate",
                interactionDefName = "Berserk",
                interactionLabel = "berserk",
                gameContext = "mental_state=Berserk",
                solo = true
            };

            DiaryPromptRequest request = BuildRequest(diaryEvent);

            PawnDiaryRimTestScope.Require(
                request != null && request.policy != null && request.policy.group != null,
                "BuildPromptRequest returned no policy snapshot for the mental-state event.");
            PawnDiaryRimTestScope.Require(
                string.Equals(request.policy.group.domain, DiaryEventDomainClassifier.MentalState, StringComparison.Ordinal),
                "The 'mental_state=' marker must classify the event into the MentalState domain, got '" + request.policy.group.domain + "'.");
            PawnDiaryRimTestScope.Require(
                string.Equals(request.policy.group.classifierKey, "Berserk", StringComparison.Ordinal),
                "A plain mental-state event classifies by its source defName; expected 'Berserk', got '" + request.policy.group.classifierKey + "'.");
            PawnDiaryRimTestScope.Require(
                string.Equals(request.policy.group.eventPromptKey, DiaryEventDomainClassifier.MentalState, StringComparison.Ordinal),
                "With no defName- or group-specific policy loaded, the event must fall back to the 'MentalState' domain key, got '" + request.policy.group.eventPromptKey + "'.");
        }

        /// <summary>
        /// §4.1 case (c) variant: an unknown/modded interaction defName that no group claims still
        /// resolves cleanly — through the Interaction catch-all group (which has no policy Def of its
        /// own) down to the broad "Interaction" domain fallback. This is the guarantee that a foreign
        /// source can never leave an event without a prompt policy.
        /// </summary>
        [Test]
        public static void UnknownModdedInteractionResolvesToInteractionDomainFallback()
        {
            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = "pawndiary-test-unknown-interaction",
                interactionDefName = "ZZZ_ModdedInteractionThatNoGroupClaims",
                interactionLabel = "some modded chatter",
                gameContext = string.Empty,
                solo = false
            };

            DiaryPromptRequest request = BuildRequest(diaryEvent);

            PawnDiaryRimTestScope.Require(
                request != null && request.policy != null && request.policy.group != null,
                "BuildPromptRequest returned no policy snapshot for the unknown interaction event.");
            PawnDiaryRimTestScope.Require(
                string.Equals(request.policy.group.domain, DiaryEventDomainClassifier.Interaction, StringComparison.Ordinal),
                "A no-marker event must classify into the Interaction domain, got '" + request.policy.group.domain + "'.");
            PawnDiaryRimTestScope.Require(
                string.Equals(request.policy.group.eventPromptKey, DiaryEventDomainClassifier.Interaction, StringComparison.Ordinal),
                "An unknown modded interaction defName must land on the safe 'Interaction' domain fallback, got '" + request.policy.group.eventPromptKey + "'.");
        }

        /// <summary>
        /// End-to-end confirmation that the LIVE capture pipeline resolves the same policy: firing a real
        /// vanilla Berserk mental state through Pawn Diary's Harmony hook (with a generation-enabled pawn
        /// in prompt-test mode) stamps a prompt-only prompt on the solo event. The capture asserts
        /// prompt-only status and returns a non-empty combined system+user prompt that names the POV pawn.
        /// </summary>
        [Test]
        public static void FiredMentalStateSoloEventCapturesPromptOnly()
        {
            MentalStateDef stateDef = RequireDef<MentalStateDef>("Berserk");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () =>
                {
                    bool started = pawn.mindState.mentalStateHandler.TryStartMentalState(
                        stateDef,
                        "Pawn Diary RimTest prompt-policy fixture",
                        forced: true);
                    PawnDiaryRimTestScope.Require(
                        started, "Vanilla refused to start the forced Berserk mental state.");
                },
                "Berserk",
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);

            // CapturedPrompt asserts the initiator POV captured as prompt-only (no network) and is
            // non-empty; the returned string is the combined system+user prompt.
            string prompt = scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole);

            string povName = diaryEvent.initiatorName;
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(povName) && prompt.IndexOf(povName, StringComparison.Ordinal) >= 0,
                "The captured mental-state prompt should render the POV pawn's name as a structured field.");
        }

        // Builds the typed prompt request for a constructed event via the internal impure adapter, using
        // neutral placeholders for the persona/enchantment inputs that key resolution does not depend on.
        private static DiaryPromptRequest BuildRequest(DiaryEvent diaryEvent)
        {
            return DiaryPipelineAdapters.BuildPromptRequest(
                diaryEvent,
                DiaryEvent.InitiatorRole,
                string.Empty,   // personaRule
                string.Empty,   // psychotypeRule
                string.Empty,   // promptEnchantment
                string.Empty,   // humorCue
                null,           // priorInitiatorEntry
                null,           // entryText
                false,          // titleRequest
                0);             // maxTokens
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
    }
}

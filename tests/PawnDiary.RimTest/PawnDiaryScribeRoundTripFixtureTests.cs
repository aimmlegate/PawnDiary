// In-game save/load fixture for Pawn Diary's persisted model objects (TEST_COVERAGE_PLAN.md §6.4).
// RimTest Redux discovers this static suite by reflection after RimWorld has loaded all active mods.
//
// Unlike the flow suites, these tests need NO colony and create NO pawns: they build representative
// DiaryEvent / ArchivedDiaryEntry / PawnDiaryRecord instances directly in code and round-trip each one
// through RimWorld's real Scribe to a throwaway temp file. That exercises the production ExposeData +
// PostLoadInit (NormalizeOnLoad) paths exactly as a real game save would, without saving a whole game.
//
// New to C#/RimWorld? A few idioms used below:
//   - These model types persist by value/string (no LookMode.Reference to live Pawns), so a single
//     IExposable can be scribed on its own to a standalone file and reloaded — the technique this suite
//     relies on. Scribe.saver/loader own a global cursor + a Scribe.mode flag; every round-trip here
//     runs its finalizers in a finally and calls Scribe.ForceStop() if anything left the mode dirty, so
//     one failed assertion can never strand the shared Scribe machinery for the next test.
//   - The per-POV prompt has no public getter on DiaryEvent (only SetPrompt), so the one prompt read-back
//     uses reflection on the private DiaryEvent.PromptFor(role), mirroring PawnDiaryRimTestScope.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Round-trips Pawn Diary's saved model objects through the real Scribe and asserts that every
    /// persisted key survives and that PostLoadInit normalization (non-null lists, blank/pending status
    /// collapse) runs. Standalone pure tests cover the normalization math; this suite proves the actual
    /// ExposeData wiring saves and reloads the shapes the game writes.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryScribeRoundTripFixtureTests
    {
        // Reflection handle for the one persisted per-POV field with no public getter. Resolved once;
        // a null handle means the private reader was renamed and the assertion must fail loudly.
        private static readonly MethodInfo PromptForMethod =
            typeof(DiaryEvent).GetMethod(
                "PromptFor",
                BindingFlags.Instance | BindingFlags.NonPublic);

        // ---- DiaryEvent round-trips ----------------------------------------------------------------

        /// <summary>
        /// A completed solo event (single initiator POV, e.g. a mental break) keeps its event-level keys
        /// and its initiator prompt/generated text/title/LLM lane across a Scribe save+load.
        /// </summary>
        [Test]
        public static void CompletedSoloEventRoundTripsAllKeys()
        {
            DiaryEvent original = new DiaryEvent
            {
                eventId = "evt_solo_complete",
                tick = 123456,
                date = "5th Aprimay 5502",
                interactionDefName = "BingingAlcohol",
                playLogInteractionDefName = string.Empty,
                interactionLabel = "mental break",
                gameContext = "mental_state=BingingAlcohol; source=test",
                colorCue = DiaryEvent.MentalBreakColorCue,
                solo = true,
                playLogEntryIds = new List<int> { 7 },
                initiatorPawnId = "Thing_Human_Solo",
                initiatorName = "Sable",
                initiatorGeneratedText = "I drank until the numbers stopped mattering.",
                initiatorStatus = DiaryEvent.CompleteStatus,
                initiatorTitle = "A wasted night",
            };
            original.SetPrompt(DiaryEvent.InitiatorRole, "SYSTEM: write a diary entry\nUSER: you binged");
            original.SetLlmMeta(DiaryEvent.InitiatorRole, "http://localhost:11434", "test-model");

            DiaryEvent loaded = ScribeRoundTrip(original);

            AssertEventLevel(original, loaded);
            Require(loaded.solo, "solo flag did not survive as true.");
            AssertPovComplete(original, loaded, DiaryEvent.InitiatorRole, "initiator");
            AssertStr("A wasted night", loaded.initiatorTitle, "initiatorTitle (titled)");
        }

        /// <summary>
        /// A completed pair event (two POVs) keeps BOTH pawns' identity, status, generated text, title,
        /// and LLM lane, plus the shared play-log id list. The recipient is intentionally left untitled
        /// so the titled/untitled contrast round-trips in one fixture.
        /// </summary>
        [Test]
        public static void CompletedPairEventRoundTripsBothPovs()
        {
            DiaryEvent original = new DiaryEvent
            {
                eventId = "evt_pair_complete",
                tick = 200000,
                date = "6th Aprimay 5502",
                interactionDefName = "smalltalk_batch",
                playLogInteractionDefName = "Chitchat",
                interactionLabel = "small talk",
                gameContext = "source=interaction; batch=true",
                colorCue = DiaryEvent.WhiteColorCue,
                solo = false,
                playLogEntryIds = new List<int> { 5, 9, 13 },
                initiatorPawnId = "Thing_Human_Init",
                initiatorName = "Ari",
                initiatorGeneratedText = "We talked about the crops; Bo seemed calmer than usual.",
                initiatorStatus = DiaryEvent.CompleteStatus,
                initiatorTitle = "Talking crops",
                recipientPawnId = "Thing_Human_Recip",
                recipientName = "Bo",
                recipientGeneratedText = "Ari found me in the field. Small words, but they helped.",
                recipientStatus = DiaryEvent.CompleteStatus,
                // recipientTitle intentionally left unset (untitled).
            };
            original.SetPrompt(DiaryEvent.InitiatorRole, "init-prompt");
            original.SetPrompt(DiaryEvent.RecipientRole, "recip-prompt");
            original.initiatorBeliefContext =
                "ideoligion: Ember Path\nrelevant precept: crafted replacements are welcomed";
            original.recipientBeliefContext =
                "ideoligion: Quiet Current\nrelevant precept: crafted replacements are despised";
            original.SetLlmMeta(DiaryEvent.InitiatorRole, "http://a", "model-a");
            original.SetLlmMeta(DiaryEvent.RecipientRole, "http://b", "model-b");

            DiaryEvent loaded = ScribeRoundTrip(original);

            AssertEventLevel(original, loaded);
            Require(!loaded.solo, "pair event should not be solo after load.");
            AssertPovComplete(original, loaded, DiaryEvent.InitiatorRole, "initiator");
            AssertPovComplete(original, loaded, DiaryEvent.RecipientRole, "recipient");
            AssertStr("Talking crops", loaded.initiatorTitle, "initiatorTitle (titled)");
            AssertStr(string.Empty, loaded.recipientTitle, "recipientTitle (untitled normalizes to empty)");
            AssertStr(original.initiatorBeliefContext, loaded.initiatorBeliefContext,
                "initiatorBeliefContext (event-time block)");
            AssertStr(original.recipientBeliefContext, loaded.recipientBeliefContext,
                "recipientBeliefContext (event-time block)");
        }

        /// <summary>
        /// Phase 1 belief rows are re-sanitized and bounded in the real PostLoadInit path. Unknown
        /// labels disappear, markup/control characters cannot survive, and a second round-trip is
        /// byte-identical to the first normalized result.
        /// </summary>
        [Test]
        public static void BeliefContextNormalizesAndStabilizesAcrossScribe()
        {
            DiaryEvent original = new DiaryEvent
            {
                eventId = "evt_belief_normalize",
                tick = 210000,
                date = "6th Aprimay 5502",
                interactionDefName = "SyntheticBelief",
                interactionLabel = "belief fixture",
                solo = true,
                initiatorPawnId = "Thing_Human_Belief",
                initiatorName = "Ari",
                initiatorBeliefContext =
                    "ideoligion: <b>Ember</b> Path\r\n"
                    + "unknown injected label: discard me\r\n"
                    + "relevant precept: crafted\u0007 replacements are welcomed"
            };

            DiaryEvent loaded = ScribeRoundTrip(original);
            string once = loaded.initiatorBeliefContext;
            Require(once.Contains("ideoligion: Ember Path"),
                "belief normalization lost the allowed ideoligion fact.");
            Require(once.Contains("relevant precept: crafted replacements are welcomed"),
                "belief normalization lost the allowed relevant-precept fact.");
            Require(!once.Contains("unknown injected label") && once.IndexOf('<') < 0
                    && once.IndexOf('\u0007') < 0,
                "belief normalization retained an unknown label, markup, or control character.");

            DiaryEvent loadedAgain = ScribeRoundTrip(loaded);
            AssertStr(once, loadedAgain.initiatorBeliefContext,
                "beliefContext must be byte-identical after a second Scribe round-trip");
            Require(once.Length <= BeliefPolicySnapshot.CreateDefault().maximumTotalCharacters,
                "beliefContext exceeded its persisted hard character bound.");
        }

        /// <summary>
        /// A pending POV has no persisted in-flight request, so ExposeData's PostLoadInit collapses a
        /// saved "pending" status (with no generated text) back to "not_generated" so the hot-window
        /// scanner can requeue it. The prompt that was captured before dispatch still survives.
        /// </summary>
        [Test]
        public static void PendingEventNormalizesToNotGeneratedOnLoad()
        {
            DiaryEvent original = new DiaryEvent
            {
                eventId = "evt_pending",
                tick = 300000,
                date = "7th Aprimay 5502",
                interactionDefName = "DeepTalk",
                interactionLabel = "deep talk",
                gameContext = "source=interaction",
                colorCue = DiaryEvent.WhiteColorCue,
                solo = false,
                initiatorPawnId = "Thing_Human_Pending",
                initiatorName = "Cai",
                initiatorStatus = DiaryEvent.PendingStatus,
            };
            original.SetPrompt(DiaryEvent.InitiatorRole, "captured-before-dispatch");

            DiaryEvent loaded = ScribeRoundTrip(original);

            AssertStr(
                DiaryEvent.NotGeneratedStatus,
                loaded.initiatorStatus,
                "pending status should normalize to not_generated on load");
            AssertStr(
                "captured-before-dispatch",
                PromptFor(loaded, DiaryEvent.InitiatorRole),
                "initiator prompt should survive a pending round-trip");
        }

        /// <summary>
        /// A failed POV keeps its "failed" status (it is neither pending nor blank, and has no generated
        /// text to upgrade it), so background sweeps do not silently retry a genuinely failed page.
        /// </summary>
        [Test]
        public static void FailedEventRoundTripsFailedStatus()
        {
            DiaryEvent original = new DiaryEvent
            {
                eventId = "evt_failed",
                tick = 400000,
                date = "8th Aprimay 5502",
                interactionDefName = "Insult",
                interactionLabel = "insult",
                gameContext = "source=interaction",
                colorCue = DiaryEvent.WhiteColorCue,
                initiatorPawnId = "Thing_Human_Failed",
                initiatorName = "Dun",
            };
            original.SetPrompt(DiaryEvent.InitiatorRole, "failed-prompt");
            original.MarkFailed(DiaryEvent.InitiatorRole, "provider returned 500");

            DiaryEvent loaded = ScribeRoundTrip(original);

            AssertStr(DiaryEvent.FailedStatus, loaded.initiatorStatus, "failed status should survive load");
            AssertStr(
                "failed-prompt",
                PromptFor(loaded, DiaryEvent.InitiatorRole),
                "initiator prompt should survive a failed round-trip");
        }

        /// <summary>
        /// A prompt-only POV (captured for inspection, never sent) keeps its "prompt_only" status and the
        /// captured prompt, so the capture is never mistaken for a queued or completed entry.
        /// </summary>
        [Test]
        public static void PromptOnlyEventRoundTripsPromptAndStatus()
        {
            DiaryEvent original = new DiaryEvent
            {
                eventId = "evt_prompt_only",
                tick = 500000,
                date = "9th Aprimay 5502",
                interactionDefName = "Chitchat",
                interactionLabel = "chitchat",
                gameContext = "source=interaction",
                colorCue = DiaryEvent.WhiteColorCue,
                initiatorPawnId = "Thing_Human_PromptOnly",
                initiatorName = "Ela",
            };
            original.SetPrompt(DiaryEvent.InitiatorRole, "this is the whole captured prompt");
            original.MarkPromptOnly(DiaryEvent.InitiatorRole, "prompt test mode");

            DiaryEvent loaded = ScribeRoundTrip(original);

            AssertStr(
                DiaryEvent.PromptOnlyStatus,
                loaded.initiatorStatus,
                "prompt_only status should survive load");
            AssertStr(
                "this is the whole captured prompt",
                PromptFor(loaded, DiaryEvent.InitiatorRole),
                "the captured prompt should survive a prompt-only round-trip");
        }

        /// <summary>
        /// A neutral colony-arrival page persists its neutral-slot generation state and the gameContext
        /// markers that make it an arrival page, so IsArrivalDescriptionFor still resolves after load.
        /// </summary>
        [Test]
        public static void NeutralArrivalEventRoundTrips()
        {
            const string arrivalPawnId = "Thing_Human_Arrival";
            DiaryEvent original = new DiaryEvent
            {
                eventId = "evt_arrival",
                tick = 600000,
                date = "1st Aprimay 5502",
                interactionDefName = "PawnDiary_Arrival",
                interactionLabel = "arrival",
                gameContext = "arrival_description=true; arrival_pawn_id=" + arrivalPawnId,
                colorCue = DiaryEvent.WhiteColorCue,
                solo = true,
                neutralGeneratedText = "A stranger walked through the gate and called this place home.",
                neutralStatus = DiaryEvent.CompleteStatus,
                neutralTitle = "A new face",
            };
            original.SetPrompt(DiaryEvent.NeutralRole, "arrival-prompt");
            original.SetLlmMeta(DiaryEvent.NeutralRole, "http://neutral", "neutral-model");

            DiaryEvent loaded = ScribeRoundTrip(original);

            AssertEventLevel(original, loaded);
            AssertStr(original.neutralGeneratedText, loaded.neutralGeneratedText, "neutralGeneratedText");
            AssertStr(DiaryEvent.CompleteStatus, loaded.neutralStatus, "neutralStatus");
            AssertStr("A new face", loaded.neutralTitle, "neutralTitle");
            AssertStr(original.neutralLlmEndpoint, loaded.neutralLlmEndpoint, "neutralLlmEndpoint");
            AssertStr(original.neutralLlmModel, loaded.neutralLlmModel, "neutralLlmModel");
            AssertStr("arrival-prompt", PromptFor(loaded, DiaryEvent.NeutralRole), "neutral prompt");
            Require(
                loaded.HasArrivalDescription(),
                "arrival gameContext marker did not survive: HasArrivalDescription() is false.");
            Require(
                loaded.IsArrivalDescriptionFor(arrivalPawnId),
                "arrival_pawn_id did not survive: IsArrivalDescriptionFor() is false.");
        }

        /// <summary>
        /// A neutral colonist-death page persists the same way as arrival, and its death markers still
        /// resolve after load so the memorial page renders instead of a dead pawn's first-person entry.
        /// </summary>
        [Test]
        public static void NeutralDeathEventRoundTrips()
        {
            const string victimId = "Thing_Human_Victim";
            DiaryEvent original = new DiaryEvent
            {
                eventId = "evt_death",
                tick = 700000,
                date = "15th Decembary 5502",
                interactionDefName = "PawnDiary_Death",
                interactionLabel = "death",
                gameContext = "death_description=true; death_victim_id=" + victimId,
                colorCue = DiaryEvent.WhiteColorCue,
                solo = true,
                neutralGeneratedText = "The colony buried one of its own today.",
                neutralStatus = DiaryEvent.CompleteStatus,
            };

            DiaryEvent loaded = ScribeRoundTrip(original);

            AssertEventLevel(original, loaded);
            AssertStr(original.neutralGeneratedText, loaded.neutralGeneratedText, "neutralGeneratedText");
            Require(
                loaded.HasDeathDescription(),
                "death gameContext marker did not survive: HasDeathDescription() is false.");
            Require(
                loaded.IsDeathDescriptionFor(victimId),
                "death_victim_id did not survive: IsDeathDescriptionFor() is false.");
        }

        /// <summary>
        /// PostLoadInit non-null normalization: a null playLogEntryIds list loads as an empty (non-null)
        /// list, and a blank per-POV status with no generated text loads as "not_generated".
        /// </summary>
        [Test]
        public static void NullListAndBlankStatusNormalizeOnLoad()
        {
            DiaryEvent original = new DiaryEvent
            {
                eventId = "evt_normalize",
                tick = 800000,
                date = "3rd Aprimay 5502",
                interactionDefName = "Chitchat",
                interactionLabel = "chitchat",
                gameContext = "source=interaction",
                colorCue = DiaryEvent.WhiteColorCue,
                initiatorPawnId = "Thing_Human_Norm",
                initiatorName = "Fen",
                initiatorStatus = null, // blank status, no generated text -> not_generated on load
                playLogEntryIds = null, // must come back as an empty, non-null list
            };

            DiaryEvent loaded = ScribeRoundTrip(original);

            Require(loaded.playLogEntryIds != null, "null playLogEntryIds should load as a non-null list.");
            Require(loaded.playLogEntryIds.Count == 0, "null playLogEntryIds should load as an EMPTY list.");
            AssertStr(
                DiaryEvent.NotGeneratedStatus,
                loaded.initiatorStatus,
                "blank status with no generated text should normalize to not_generated");
        }

        // ---- Narrative Continuity N1 round-trips ---------------------------------------------------

        /// <summary>
        /// N1's additive POV fields survive a real Scribe round-trip, while a legacy event without the
        /// new keys normalizes to safe empty collections/context. Compact archive rows retain only the
        /// prose-free references and selected-key history needed by the cold-history indexes.
        /// </summary>
        [Test]
        public static void NarrativeContinuityRoundTripsAndLegacyRowsStayEmpty()
        {
            DiaryEvent original = new DiaryEvent
            {
                eventId = "evt_narrative_roundtrip",
                tick = 456789,
                date = "8th Aprimay 5502",
                interactionDefName = "Chat",
                interactionLabel = "chat",
                gameContext = "source=rimtest_narrative",
                colorCue = DiaryEvent.QuietColorCue,
                solo = true,
                initiatorPawnId = "Thing_Human_Narrative",
                initiatorName = "Mira",
            };
            NarrativeEvidence evidence = new NarrativeEvidence
            {
                facet = NarrativeFacetTokens.IdentityTransition,
                phase = "opened",
                subjectKind = NarrativeSubjectKindTokens.Pawn,
                subjectId = "Thing_Human_Narrative",
                subjectLabel = "Mira",
                arcKey = "core|fixture",
                beliefTopics = new List<string> { "belonging" },
                salience = NarrativeSalienceTokens.Meaningful,
                pawnCanKnow = true,
                sourceDomain = "core",
                sourceDefName = "RimTestFixture",
            };
            NarrativeReference reference = NarrativeReferencePolicy.FromEvidence(evidence);
            original.ApplyNarrativeContext(DiaryEvent.InitiatorRole, new NarrativeContextBuildResult
            {
                evidence = new List<NarrativeEvidence> { evidence },
                selection = new NarrativeContextSelection
                {
                    narrativeContext = "Mira still measures this work against the promise she made herself.",
                    references = new List<NarrativeReference> { reference },
                    selectedCandidates = new List<NarrativeLensCandidate>
                    {
                        new NarrativeLensCandidate { candidateKey = "core-fixture-identity" }
                    }
                }
            });

            DiaryEvent loaded = ScribeRoundTrip(original);
            Require(loaded.NarrativeEvidenceForRole(DiaryEvent.InitiatorRole).Count == 1,
                "Narrative evidence should survive its additive POV save key.");
            Require(loaded.NarrativeReferencesForRole(DiaryEvent.InitiatorRole).Count == 1,
                "Narrative references should survive their additive POV save key.");
            Require(loaded.NarrativeSelectedCandidateKeysForRole(DiaryEvent.InitiatorRole).Count == 1,
                "Narrative selection keys should survive their additive POV save key.");
            AssertStr("Mira still measures this work against the promise she made herself.",
                loaded.NarrativeContextForRole(DiaryEvent.InitiatorRole), "narrative context");

            DiaryEvent legacyLoaded = ScribeRoundTrip(new DiaryEvent
            {
                eventId = "evt_narrative_legacy",
                tick = 456790,
                date = "8th Aprimay 5502",
                interactionDefName = "Chat",
                interactionLabel = "chat",
                gameContext = "source=rimtest_narrative",
                colorCue = DiaryEvent.QuietColorCue,
                solo = true,
                initiatorPawnId = "Thing_Human_Legacy",
                initiatorName = "Old Save",
            });
            Require(legacyLoaded.NarrativeEvidenceForRole(DiaryEvent.InitiatorRole).Count == 0
                    && legacyLoaded.NarrativeReferencesForRole(DiaryEvent.InitiatorRole).Count == 0
                    && legacyLoaded.NarrativeSelectedCandidateKeysForRole(DiaryEvent.InitiatorRole).Count == 0,
                "A pre-N1 save must load empty Narrative Continuity collections.");
            AssertStr(string.Empty, legacyLoaded.NarrativeContextForRole(DiaryEvent.InitiatorRole),
                "legacy narrative context");

            ArchivedDiaryEntry archiveLoaded = ScribeRoundTrip(new ArchivedDiaryEntry
            {
                eventId = "evt_narrative_archive",
                pawnId = "Thing_Human_Narrative",
                povRole = DiaryEvent.InitiatorRole,
                tick = 456789,
                date = "8th Aprimay 5502",
                status = DiaryEvent.CompleteStatus,
                narrativeReferences = NarrativeStatePersistence.FromReferences(
                    new List<NarrativeReference> { reference }),
                narrativeSelectedCandidateKeys = new List<string> { "core-fixture-identity" },
            });
            Require(NarrativeStatePersistence.ToReferences(archiveLoaded.narrativeReferences).Count == 1,
                "An archive row should retain its compact narrative reference.");
            Require(archiveLoaded.narrativeSelectedCandidateKeys.Count == 1,
                "An archive row should retain its compact selected-key history.");
        }

        // ---- ArchivedDiaryEntry round-trip ---------------------------------------------------------

        /// <summary>
        /// A compact archived page keeps its identity, display text, status, LLM model, title, year, and
        /// classification flags, and a null play-log id list normalizes to an empty list on load.
        /// </summary>
        [Test]
        public static void ArchivedDiaryEntryRoundTrips()
        {
            ArchivedDiaryEntry original = new ArchivedDiaryEntry
            {
                eventId = "evt_archived",
                playLogEntryIds = new List<int> { 11, 22 },
                pawnId = "Thing_Human_Arch",
                povRole = DiaryEvent.InitiatorRole,
                tick = 900000,
                date = "10th Aprimay 5502",
                year = 5502,
                text = "raw game text",
                generatedText = "The old page, still legible after all this time.",
                status = DiaryEvent.CompleteStatus,
                llmModel = "archive-model",
                title = "An old page",
                archivedGenerationStale = false,
                groupLabel = "Small talk",
                interactionDefName = "smalltalk_batch",
                interactionLabel = "small talk",
                colorCue = DiaryEvent.WhiteColorCue,
                important = false,
                staggeredIntensity = 2,
                distortDirectSpeech = false,
                arrivalDescription = false,
                deathDescription = false,
                linkedPawnId = "Thing_Human_Other",
                linkedPawnName = "Other",
                linkedRole = DiaryEvent.RecipientRole,
                linkedPreviewText = "a glimpse of the other side",
                linkedGenerated = true,
                linkedTitle = "Linked",
            };

            ArchivedDiaryEntry loaded = ScribeRoundTrip(original);

            AssertStr(original.eventId, loaded.eventId, "archive eventId");
            AssertStr(original.pawnId, loaded.pawnId, "archive pawnId");
            AssertStr(original.povRole, loaded.povRole, "archive povRole");
            AssertInt(original.tick, loaded.tick, "archive tick");
            AssertStr(original.date, loaded.date, "archive date");
            AssertInt(original.year, loaded.year, "archive year");
            AssertStr(original.text, loaded.text, "archive text");
            AssertStr(original.generatedText, loaded.generatedText, "archive generatedText");
            AssertStr(DiaryEvent.CompleteStatus, loaded.status, "archive status");
            AssertStr(original.llmModel, loaded.llmModel, "archive llmModel");
            AssertStr(original.title, loaded.title, "archive title");
            AssertStr(original.groupLabel, loaded.groupLabel, "archive groupLabel");
            AssertStr(original.interactionDefName, loaded.interactionDefName, "archive interactionDefName");
            AssertStr(original.colorCue, loaded.colorCue, "archive colorCue");
            AssertInt(original.staggeredIntensity, loaded.staggeredIntensity, "archive staggeredIntensity");
            Require(!loaded.important, "archive important=false should survive load.");
            AssertIntList(original.playLogEntryIds, loaded.playLogEntryIds, "archive playLogEntryIds");
            AssertStr(original.linkedPawnId, loaded.linkedPawnId, "archive linkedPawnId");
            AssertStr(original.linkedPreviewText, loaded.linkedPreviewText, "archive linkedPreviewText");
            Require(loaded.linkedGenerated, "archive linkedGenerated=true should survive load.");

            // Null play-log list normalizes to a non-null empty list on load.
            ArchivedDiaryEntry nullListOriginal = new ArchivedDiaryEntry
            {
                eventId = "evt_archived_nulllist",
                playLogEntryIds = null,
                pawnId = "Thing_Human_Arch2",
                povRole = DiaryEvent.NeutralRole,
                tick = 950000,
                date = "11th Aprimay 5502",
                year = 5502,
            };
            ArchivedDiaryEntry nullListLoaded = ScribeRoundTrip(nullListOriginal);
            Require(
                nullListLoaded.playLogEntryIds != null && nullListLoaded.playLogEntryIds.Count == 0,
                "archive null playLogEntryIds should load as a non-null empty list.");
        }

        // ---- PawnDiaryRecord round-trip ------------------------------------------------------------

        /// <summary>
        /// A postponed Biotech growth letter keeps its detached before-state without saving a live
        /// ChoiceLetter/Pawn reference, including selected trait/skill facts needed after reload.
        /// </summary>
        [Test]
        public static void PendingBiotechGrowthMomentRoundTrips()
        {
            PendingBiotechGrowthMoment original = new PendingBiotechGrowthMoment
            {
                pawnId = "Thing_Human_Growth",
                birthdayAge = 10,
                birthdayTick = 12345,
                configuredTick = 12346,
                growthTier = 6,
                newResponsibilities = true,
                correlationId = "growth|Thing_Human_Growth|10",
                familyArcId = "biotech-family|Thing_Human_Growth",
                birthdaySnapshot = new GrowthPawnSnapshot
                {
                    pawnId = "Thing_Human_Growth",
                    displayName = "Ira",
                    biologicalAge = 10,
                    growthTier = 6,
                    shortName = "Ira",
                    capturedTick = 12345,
                    traits = new List<GrowthTraitFact>
                    {
                        new GrowthTraitFact { traitKey = "Kind|0", label = "kind", description = "kind-hearted" }
                    },
                    skills = new List<GrowthSkillFact>
                    {
                        new GrowthSkillFact
                        {
                            skillDefName = "Plants",
                            label = "plants",
                            passion = BiotechPassionTokens.Minor,
                            level = 8
                        }
                    }
                }
            };

            PendingBiotechGrowthMoment loaded = ScribeRoundTrip(original);
            AssertStr(original.pawnId, loaded.pawnId, "pending growth pawnId");
            AssertInt(10, loaded.birthdayAge, "pending growth birthdayAge");
            AssertInt(12345, loaded.birthdayTick, "pending growth birthdayTick");
            AssertInt(6, loaded.growthTier, "pending growth tier");
            Require(loaded.newResponsibilities, "pending growth responsibility flag should survive.");
            AssertStr(original.correlationId, loaded.correlationId, "pending growth correlationId");
            AssertStr(original.familyArcId, loaded.familyArcId, "pending growth familyArcId");
            Require(loaded.birthdaySnapshot != null, "pending growth snapshot should survive.");
            AssertStr("Ira", loaded.birthdaySnapshot.shortName, "pending growth shortName");
            Require(loaded.birthdaySnapshot.traits.Count == 1, "pending growth trait facts should survive.");
            Require(loaded.birthdaySnapshot.skills.Count == 1, "pending growth skill facts should survive.");
            AssertStr(BiotechPassionTokens.Minor, loaded.birthdaySnapshot.skills[0].passion,
                "pending growth passion token");
        }

        /// <summary>
        /// A canonical birth waiting on naming keeps exact outcome/participant/writer facts without
        /// saving any live Pawn, Corpse, letter, ritual, or correlation-scope reference.
        /// </summary>
        [Test]
        public static void PendingBiotechBirthRoundTrips()
        {
            PendingBiotechBirthState original = new PendingBiotechBirthState
            {
                createdTick = 22000,
                snapshot = new BirthMutationSnapshot
                {
                    familyArcId = "biotech-family|Thing_Birther|Hediff_22",
                    childId = "Thing_Child",
                    currentChildName = "Baby 1",
                    birther = new FamilyParticipantFact
                    {
                        pawnId = "Thing_Birther",
                        displayName = "Ari",
                        roleToken = BiotechFamilyRoleTokens.Birther,
                        eligible = true
                    },
                    geneticMother = new FamilyParticipantFact
                    {
                        pawnId = "Thing_Mother",
                        displayName = "Bo",
                        roleToken = BiotechFamilyRoleTokens.GeneticMother,
                        eligible = true
                    },
                    father = new FamilyParticipantFact
                    {
                        pawnId = "Thing_Father",
                        displayName = "Cy",
                        roleToken = BiotechFamilyRoleTokens.Father,
                        eligible = true
                    },
                    outcomeToken = BiotechBirthOutcomeTokens.Healthy,
                    methodToken = BiotechBirthMethodTokens.Surrogacy,
                    ritualBirth = true,
                    namingDeadline = 82000,
                    birthTick = 22000,
                    correlationId = "birth|biotech-family|Thing_Birther|Hediff_22"
                },
                writers = new BirthWriterSelection
                {
                    writers = new List<BirthWriterFact>
                    {
                        new BirthWriterFact
                        {
                            pawnId = "Thing_Birther",
                            displayName = "Ari",
                            roleToken = BiotechFamilyRoleTokens.Birther
                        },
                        new BirthWriterFact
                        {
                            pawnId = "Thing_Mother",
                            displayName = "Bo",
                            roleToken = BiotechFamilyRoleTokens.GeneticMother
                        }
                    }
                },
                eventContext = new BirthEventContextSnapshot
                {
                    birthTick = 22000,
                    birthDate = "4th Aprimay 5500",
                    writers = new List<BirthWriterContextSnapshot>
                    {
                        new BirthWriterContextSnapshot
                        {
                            pawnId = "Thing_Birther",
                            displayName = "Ari",
                            pawnSummary = "Ari at the birth boundary",
                            surroundings = "a hospital room",
                            continuity = "parent of Baby 1",
                            pairContinuity = "partner of Bo",
                            lastOpener = "Before today",
                            previousEntryEnding = "I hoped we were ready.",
                            weapon = "none",
                            staggeredIntensity = 2,
                            textDecorationFacts = "hediff=Labor",
                            skipFirstPersonGeneration = true
                        }
                    }
                }
            };

            PendingBiotechBirthState loaded = ScribeRoundTrip(original);
            Require(loaded.snapshot != null, "pending birth snapshot should survive.");
            Require(loaded.writers?.writers != null && loaded.writers.writers.Count == 2,
                "pending birth writers should survive.");
            AssertStr(original.snapshot.familyArcId, loaded.snapshot.familyArcId,
                "pending birth family arc");
            AssertStr(original.snapshot.childId, loaded.snapshot.childId, "pending birth child");
            AssertStr(BiotechBirthOutcomeTokens.Healthy, loaded.snapshot.outcomeToken,
                "pending birth outcome");
            AssertStr(BiotechBirthMethodTokens.Surrogacy, loaded.snapshot.methodToken,
                "pending birth method");
            Require(loaded.snapshot.ritualBirth, "pending ritual-birth flag should survive.");
            AssertInt(82000, loaded.snapshot.namingDeadline, "pending birth naming deadline");
            AssertStr(BiotechFamilyRoleTokens.Birther, loaded.writers.writers[0].roleToken,
                "pending first writer role");
            AssertStr(BiotechFamilyRoleTokens.GeneticMother, loaded.writers.writers[1].roleToken,
                "pending second writer role");
            Require(loaded.eventContext?.writers != null && loaded.eventContext.writers.Count == 1,
                "pending birth event-time context should survive.");
            AssertInt(22000, loaded.eventContext.birthTick, "pending context birth tick");
            AssertStr("4th Aprimay 5500", loaded.eventContext.birthDate, "pending context date");
            AssertStr("Ari at the birth boundary", loaded.eventContext.writers[0].pawnSummary,
                "pending context pawn summary");
            AssertStr("partner of Bo", loaded.eventContext.writers[0].pairContinuity,
                "pending context pair continuity");
            Require(loaded.eventContext.writers[0].skipFirstPersonGeneration,
                "pending context generation eligibility should survive.");
        }

        /// <summary>
        /// A family arc keeps stable participant/Hediff IDs, bounded supporter counters, summarized
        /// deltas, growth ages, and compaction state without retaining live Pawn references.
        /// </summary>
        [Test]
        public static void BiotechFamilyArcRoundTrips()
        {
            BiotechFamilyArcState original = new BiotechFamilyArcState
            {
                familyArcId = "biotech-family|Thing_Child",
                pregnancyHediffId = "Hediff_11",
                laborHediffId = "Hediff_12",
                childId = "Thing_Child",
                birtherId = "Thing_Birther",
                geneticMotherId = "Thing_Mother",
                fatherId = "Thing_Father",
                currentChildName = "Jun",
                openedTick = 100,
                lastObservedTick = 900,
                baselineOnly = true,
                detailsCompacted = false,
                lastSummarizedObservationTick = 800,
                recordedGrowthAges = new List<int> { 7, 10 },
                supporters = new List<FamilySupportObservationState>
                {
                    new FamilySupportObservationState
                    {
                        adultId = "Thing_Parent",
                        lastDisplayName = "Kai",
                        relationToken = BiotechFamilyRoleTokens.Parent,
                        lessonCount = 5,
                        babyPlayCount = 2,
                        summarizedLessonCount = 4,
                        summarizedBabyPlayCount = 2,
                        firstObservedTick = 200,
                        lastObservedTick = 900
                    }
                }
            };

            BiotechFamilyArcState loaded = ScribeRoundTrip(original);
            AssertStr(original.familyArcId, loaded.familyArcId, "family arc ID");
            AssertStr(original.pregnancyHediffId, loaded.pregnancyHediffId, "family pregnancy Hediff ID");
            AssertStr(original.laborHediffId, loaded.laborHediffId, "family labor Hediff ID");
            AssertStr(original.childId, loaded.childId, "family child ID");
            AssertStr(original.birtherId, loaded.birtherId, "family birther ID");
            AssertStr(original.currentChildName, loaded.currentChildName, "family child name");
            Require(loaded.recordedGrowthAges != null && loaded.recordedGrowthAges.Count == 2,
                "family growth ages should survive.");
            Require(loaded.supporters != null && loaded.supporters.Count == 1,
                "family supporter rows should survive.");
            FamilySupportObservationState supporter = loaded.supporters[0];
            AssertStr(BiotechFamilyRoleTokens.Parent, supporter.relationToken, "family supporter role");
            AssertInt(5, supporter.lessonCount, "family supporter lessons");
            AssertInt(4, supporter.summarizedLessonCount, "family summarized lessons");
            AssertInt(2, supporter.summarizedBabyPlayCount, "family summarized play");
            AssertInt(900, supporter.lastObservedTick, "family supporter last observation tick");
        }

        /// <summary>
        /// A pawn's diary index keeps its event id list, per-pawn generation flags, and nested
        /// progression/arc-schedule state; null list/nested-state fields normalize to non-null on load.
        /// </summary>
        [Test]
        public static void PawnDiaryRecordRoundTrips()
        {
            PawnProgressionState progression = new PawnProgressionState
            {
                highestPsylinkLevelRecorded = 3,
                lastObservedXenotypeDefName = "Baseliner",
                knownTraitKeys = new List<string> { "Nimble|0", "Kind|0" },
            };
            progression.SetSkillMilestone("Shooting", 10);
            progression.SetSkillMilestone("Melee", 4);
            BiotechPawnProgressionState biotechProgression = progression.EnsureBiotechState();
            biotechProgression.ConsumeGrowthAge(7);
            biotechProgression.ConsumeGrowthAge(10);
            GeneIdentityObservationState geneObservation =
                biotechProgression.EnsureGeneIdentityObservation();
            geneObservation.geneObservationVersion = GeneIdentityObservationPolicy.CurrentVersion;
            geneObservation.xenotypeDefName = "CustomXenotype";
            geneObservation.xenotypeLabel = "Custom identity";
            geneObservation.geneDefNames = new List<string> { "Gene_Alpha", "Gene_Beta" };
            geneObservation.membershipTruncated = true;
            MechanitorObservationState mechanitor =
                biotechProgression.EnsureMechanitorObservation();
            mechanitor.observationVersion = MechanitorObservationState.CurrentVersion;
            mechanitor.mechlinkPresent = true;
            mechanitor.firstControlledPageConsumed = true;
            mechanitor.firstControlledCombatPageConsumed = true;
            mechanitor.observedMechs.Add(new MechanitorMechObservationState
            {
                mechId = "Thing_Mech_Moss",
                lastDisplayName = "Moss",
                kindDefName = "Lifter",
                firstObservedTick = 123,
                lossObserved = true
            });
            mechanitor.bossCalls.Add(new MechanitorBossCallObservationState
            {
                bossgroupDefName = "Bossgroup_Diabolus",
                bossDefName = "Boss_Diabolus",
                bossKindDefName = "Diabolus",
                bossLabel = "Diabolus",
                bossPawnId = "Thing_Boss_Diabolus_42",
                calledTick = 456,
                defeatedObserved = true
            });

            PawnArcScheduleState arc = new PawnArcScheduleState
            {
                lastArcEntryTick = 1234,
                lastArcEntryYear = 5501,
                arcEntriesThisYear = 2,
                recentlyUsedEventIds = new List<string> { "e1", "e2", "e3" },
            };

            PawnDiaryRecord original = new PawnDiaryRecord
            {
                pawnId = "Thing_Human_Record",
                pawnName = "Gale",
                eventIds = new List<string> { "evt_a", "evt_b", "evt_c" },
                favoriteEntryKeys = new List<string> { "evt_a|initiator", "evt_c|recipient" },
                diaryGenerationEnabled = false, // non-default, must round-trip
                hasUnreadGeneratedEntry = true,
                customWritingStyleRule = "Terse and grim.",
                progressionState = progression,
                arcSchedule = arc,
            };

            PawnDiaryRecord loaded = ScribeRoundTrip(original);

            AssertStr(original.pawnId, loaded.pawnId, "record pawnId");
            AssertStr(original.pawnName, loaded.pawnName, "record pawnName");
            Require(!loaded.diaryGenerationEnabled, "record diaryGenerationEnabled=false should survive load.");
            Require(loaded.hasUnreadGeneratedEntry, "record hasUnreadGeneratedEntry=true should survive load.");

            AssertStrList(original.eventIds, loaded.eventIds, "record eventIds");
            AssertStrList(original.favoriteEntryKeys, loaded.favoriteEntryKeys, "record favoriteEntryKeys");
            Require(
                !string.IsNullOrWhiteSpace(loaded.personaDefName),
                "record personaDefName should normalize to a non-empty default on load.");

            Require(loaded.progressionState != null, "record progressionState should be non-null after load.");
            AssertInt(
                3,
                loaded.progressionState.highestPsylinkLevelRecorded,
                "record progression highestPsylinkLevelRecorded");
            AssertInt(
                10,
                loaded.progressionState.HighestSkillMilestone("Shooting"),
                "record progression Shooting milestone");
            AssertInt(
                4,
                loaded.progressionState.HighestSkillMilestone("Melee"),
                "record progression Melee milestone");
            Require(
                loaded.progressionState.knownTraitKeys != null
                    && loaded.progressionState.knownTraitKeys.Count == 2,
                "record progression knownTraitKeys should survive load.");
            Require(
                loaded.progressionState.EnsureBiotechState().HasConsumedGrowthAge(7)
                    && loaded.progressionState.EnsureBiotechState().HasConsumedGrowthAge(10),
                "record nested Biotech consumed growth ages should survive load.");
            GeneIdentityObservationState loadedGeneObservation = loaded.progressionState
                .EnsureBiotechState().EnsureGeneIdentityObservation();
            AssertInt(
                GeneIdentityObservationPolicy.CurrentVersion,
                loadedGeneObservation.geneObservationVersion,
                "record nested gene observation version");
            AssertStr("CustomXenotype", loadedGeneObservation.xenotypeDefName,
                "record nested gene xenotype Def");
            AssertStr("Custom identity", loadedGeneObservation.xenotypeLabel,
                "record nested gene xenotype label");
            AssertStrList(new List<string> { "Gene_Alpha", "Gene_Beta" },
                loadedGeneObservation.geneDefNames, "record nested gene membership");
            Require(loadedGeneObservation.membershipTruncated,
                "record nested gene truncation marker should survive load.");
            MechanitorObservationState loadedMechanitor = loaded.progressionState
                .EnsureBiotechState().EnsureMechanitorObservation();
            AssertInt(MechanitorObservationState.CurrentVersion,
                loadedMechanitor.observationVersion, "record nested mechanitor observation version");
            Require(loadedMechanitor.mechlinkPresent
                    && loadedMechanitor.firstControlledPageConsumed
                    && loadedMechanitor.firstControlledCombatPageConsumed,
                "record nested mechanitor lifecycle flags should survive load.");
            Require(loadedMechanitor.observedMechs.Count == 1
                    && loadedMechanitor.observedMechs[0].lossObserved,
                "record nested observed-mech row should survive load.");
            AssertStr("Thing_Mech_Moss", loadedMechanitor.observedMechs[0].mechId,
                "record nested observed mech ID");
            Require(loadedMechanitor.bossCalls.Count == 1
                    && loadedMechanitor.bossCalls[0].defeatedObserved,
                "record nested boss call/defeat row should survive load.");
            AssertStr("Boss_Diabolus", loadedMechanitor.bossCalls[0].bossDefName,
                "record nested boss Def");
            AssertStr("Thing_Boss_Diabolus_42", loadedMechanitor.bossCalls[0].bossPawnId,
                "record nested exact boss pawn ID");

            Require(loaded.arcSchedule != null, "record arcSchedule should be non-null after load.");
            AssertInt(1234, loaded.arcSchedule.lastArcEntryTick, "record arc lastArcEntryTick");
            AssertInt(2, loaded.arcSchedule.arcEntriesThisYear, "record arc arcEntriesThisYear");
            AssertStrList(
                original.arcSchedule.recentlyUsedEventIds,
                loaded.arcSchedule.recentlyUsedEventIds,
                "record arc recentlyUsedEventIds");

            // Null list + null nested state normalize to non-null on load.
            PawnDiaryRecord nullOriginal = new PawnDiaryRecord
            {
                pawnId = "Thing_Human_Record2",
                pawnName = "Hale",
                eventIds = null,
                favoriteEntryKeys = null,
                progressionState = null,
                arcSchedule = null,
            };
            PawnDiaryRecord nullLoaded = ScribeRoundTrip(nullOriginal);
            Require(
                nullLoaded.eventIds != null && nullLoaded.eventIds.Count == 0,
                "record null eventIds should load as a non-null empty list.");
            Require(
                nullLoaded.favoriteEntryKeys != null && nullLoaded.favoriteEntryKeys.Count == 0,
                "record null favoriteEntryKeys should load as a non-null empty list.");

            // Corrupt/hand-edited favorite rows exercise the non-trivial PostLoadInit normalizer: exact
            // duplicates and blanks disappear in first-seen order, and oversized input is bounded.
            List<string> malformedFavorites = new List<string>
            {
                "duplicate|initiator",
                string.Empty,
                "duplicate|initiator"
            };
            for (int i = 0; i < DiaryEntryFilterPolicy.MaximumFavoriteEntryKeys + 4; i++)
            {
                malformedFavorites.Add("bounded_" + i + "|recipient");
            }
            PawnDiaryRecord malformedLoaded = ScribeRoundTrip(new PawnDiaryRecord
            {
                pawnId = "Thing_Human_MalformedFavorites",
                favoriteEntryKeys = malformedFavorites
            });
            Require(malformedLoaded.favoriteEntryKeys.Count
                    == DiaryEntryFilterPolicy.MaximumFavoriteEntryKeys,
                "record favoriteEntryKeys should clamp corrupt oversized saves.");
            AssertStr("duplicate|initiator", malformedLoaded.favoriteEntryKeys[0],
                "record favorite normalization first key");
            AssertStr("bounded_0|recipient", malformedLoaded.favoriteEntryKeys[1],
                "record favorite normalization first unique key");
            AssertStr("bounded_4094|recipient",
                malformedLoaded.favoriteEntryKeys[malformedLoaded.favoriteEntryKeys.Count - 1],
                "record favorite normalization last in-bound key");
            Require(nullLoaded.progressionState != null, "record null progressionState should load as non-null.");
            Require(nullLoaded.arcSchedule != null, "record null arcSchedule should load as non-null.");
            GeneIdentityObservationState oldSaveGeneObservation = nullLoaded.progressionState
                .EnsureBiotechState().EnsureGeneIdentityObservation();
            Require(oldSaveGeneObservation.geneObservationVersion == 0
                    && oldSaveGeneObservation.geneDefNames != null
                    && oldSaveGeneObservation.geneDefNames.Count == 0,
                "an old save with no gene keys should normalize to an uninitialized empty row.");
            MechanitorObservationState oldSaveMechanitor = nullLoaded.progressionState
                .EnsureBiotechState().EnsureMechanitorObservation();
            Require(oldSaveMechanitor.observationVersion == 0
                    && oldSaveMechanitor.observedMechs.Count == 0
                    && oldSaveMechanitor.bossCalls.Count == 0,
                "an old save with no mechanitor keys should remain an uninitialized silent row.");

            // A real pre-Biotech record can already have populated scalar progression state while its
            // additive nested Biotech row is absent. Prove that exact shape preserves the old fields
            // and creates only an uninitialized, silent gene observation.
            PawnDiaryRecord legacyOriginal = new PawnDiaryRecord
            {
                pawnId = "Thing_Human_LegacyProgression",
                pawnName = "Legacy",
                progressionState = new PawnProgressionState
                {
                    highestPsylinkLevelRecorded = 2,
                    biotechProgressionState = null
                }
            };
            PawnDiaryRecord legacyLoaded = ScribeRoundTrip(legacyOriginal);
            Require(legacyLoaded.progressionState != null
                    && legacyLoaded.progressionState.highestPsylinkLevelRecorded == 2,
                "a populated legacy progression row lost its pre-Biotech fields.");
            GeneIdentityObservationState legacyGeneObservation = legacyLoaded.progressionState
                .EnsureBiotechState().EnsureGeneIdentityObservation();
            Require(legacyGeneObservation.geneObservationVersion == 0
                    && legacyGeneObservation.geneDefNames.Count == 0
                    && !legacyGeneObservation.membershipTruncated,
                "a populated legacy progression row invented a current gene baseline.");
        }

        // ---- Scribe round-trip machinery -----------------------------------------------------------

        /// <summary>
        /// Saves a single IExposable to a unique temp file, reloads it into a fresh instance, and returns
        /// the reloaded object. FinalizeLoading runs ResolveAllCrossReferences + DoAllPostLoadInits, so
        /// the reloaded object's ExposeData executes once more in PostLoadInit mode (its NormalizeOnLoad).
        /// The finally block guarantees the shared Scribe state is reset and the temp file is deleted even
        /// if a Scribe call throws, so one failing round-trip can never poison the next test.
        /// </summary>
        private static T ScribeRoundTrip<T>(T toSave) where T : class, IExposable
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_rimtest_" + Guid.NewGuid().ToString("N") + ".xml");

            T loaded = null;
            try
            {
                // SAVE.
                Scribe.saver.InitSaving(path, "root");
                T saveRef = toSave;
                Scribe_Deep.Look(ref saveRef, "obj");
                Scribe.saver.FinalizeSaving();

                // LOAD (FinalizeLoading triggers the PostLoadInit pass -> NormalizeOnLoad).
                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "obj");
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                // A thrown Scribe call leaves Scribe.mode non-Inactive; reset it so the harness's global
                // cursor is clean for the next test, then always remove the temp file.
                if (Scribe.mode != LoadSaveMode.Inactive)
                {
                    Scribe.ForceStop();
                }

                DeleteTempFile(path);
            }

            if (loaded == null)
            {
                throw new AssertionException(
                    "Scribe round-trip returned a null " + typeof(T).Name + ".");
            }

            return loaded;
        }

        /// <summary>
        /// Shared loaded-fixture seam for pages produced by another suite. It still uses this suite's
        /// real Scribe saver/loader/finalizer implementation; callers cannot bypass normalization.
        /// </summary>
        internal static T ScribeRoundTripForTests<T>(T toSave) where T : class, IExposable
        {
            return ScribeRoundTrip(toSave);
        }

        private static void DeleteTempFile(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup: a locked/removed temp file must never fail the test itself.
            }
        }

        // ---- assertion helpers ---------------------------------------------------------------------

        // Asserts the shared event-level (non-POV) keys survived a DiaryEvent round-trip.
        private static void AssertEventLevel(DiaryEvent expected, DiaryEvent actual)
        {
            AssertStr(expected.eventId, actual.eventId, "eventId");
            AssertInt(expected.tick, actual.tick, "tick");
            AssertStr(expected.date, actual.date, "date");
            AssertStr(expected.interactionDefName, actual.interactionDefName, "interactionDefName");
            AssertStr(
                expected.playLogInteractionDefName ?? string.Empty,
                actual.playLogInteractionDefName ?? string.Empty,
                "playLogInteractionDefName");
            AssertStr(expected.gameContext, actual.gameContext, "gameContext");
            AssertIntList(expected.playLogEntryIds, actual.playLogEntryIds, "playLogEntryIds");
        }

        // Asserts a completed POV's pawn identity, status, generated text, and LLM lane survived.
        private static void AssertPovComplete(
            DiaryEvent expected,
            DiaryEvent actual,
            string povRole,
            string label)
        {
            AssertStr(PawnIdOf(expected, povRole), PawnIdOf(actual, povRole), label + " pawnId");
            AssertStr(NameOf(expected, povRole), NameOf(actual, povRole), label + " name");
            AssertStr(DiaryEvent.CompleteStatus, StatusOf(actual, povRole), label + " status");
            AssertStr(GeneratedTextOf(expected, povRole), GeneratedTextOf(actual, povRole), label + " generatedText");
            AssertStr(EndpointOf(expected, povRole), EndpointOf(actual, povRole), label + " llmEndpoint");
            AssertStr(ModelOf(expected, povRole), ModelOf(actual, povRole), label + " llmModel");
            AssertStr(PromptFor(expected, povRole), PromptFor(actual, povRole), label + " prompt");
        }

        private static string PawnIdOf(DiaryEvent e, string role)
        {
            return DiaryEvent.RoleEquals(role, DiaryEvent.RecipientRole) ? e.recipientPawnId : e.initiatorPawnId;
        }

        private static string NameOf(DiaryEvent e, string role)
        {
            return DiaryEvent.RoleEquals(role, DiaryEvent.RecipientRole) ? e.recipientName : e.initiatorName;
        }

        private static string StatusOf(DiaryEvent e, string role)
        {
            if (DiaryEvent.RoleEquals(role, DiaryEvent.RecipientRole))
            {
                return e.recipientStatus;
            }

            if (DiaryEvent.RoleEquals(role, DiaryEvent.NeutralRole))
            {
                return e.neutralStatus;
            }

            return e.initiatorStatus;
        }

        private static string GeneratedTextOf(DiaryEvent e, string role)
        {
            if (DiaryEvent.RoleEquals(role, DiaryEvent.RecipientRole))
            {
                return e.recipientGeneratedText;
            }

            if (DiaryEvent.RoleEquals(role, DiaryEvent.NeutralRole))
            {
                return e.neutralGeneratedText;
            }

            return e.initiatorGeneratedText;
        }

        private static string EndpointOf(DiaryEvent e, string role)
        {
            if (DiaryEvent.RoleEquals(role, DiaryEvent.RecipientRole))
            {
                return e.recipientLlmEndpoint;
            }

            if (DiaryEvent.RoleEquals(role, DiaryEvent.NeutralRole))
            {
                return e.neutralLlmEndpoint;
            }

            return e.initiatorLlmEndpoint;
        }

        private static string ModelOf(DiaryEvent e, string role)
        {
            if (DiaryEvent.RoleEquals(role, DiaryEvent.RecipientRole))
            {
                return e.recipientLlmModel;
            }

            if (DiaryEvent.RoleEquals(role, DiaryEvent.NeutralRole))
            {
                return e.neutralLlmModel;
            }

            return e.initiatorLlmModel;
        }

        // Reads the private per-POV prompt via reflection (DiaryEvent exposes SetPrompt but no getter).
        private static string PromptFor(DiaryEvent e, string role)
        {
            if (PromptForMethod == null)
            {
                throw new AssertionException(
                    "Pawn Diary test could not locate the private DiaryEvent.PromptFor(role) reader.");
            }

            return PromptForMethod.Invoke(e, new object[] { role }) as string ?? string.Empty;
        }

        private static void AssertStr(string expected, string actual, string field)
        {
            // Scribe persists strings as XML text, and XML end-of-line normalization rewrites embedded
            // \r\n <-> \n across a save/load. A diary prompt's newline STYLE is not semantically meaningful
            // and can never round-trip byte-for-byte through XML, so compare line-ending-normalized — the
            // CONTENT must still match exactly, only the newline representation is normalized.
            if (!string.Equals(NormalizeNewlines(expected), NormalizeNewlines(actual), StringComparison.Ordinal))
            {
                throw new AssertionException(
                    field + " did not survive the Scribe round-trip. expected='"
                    + (expected ?? "<null>") + "' actual='" + (actual ?? "<null>") + "'.");
            }
        }

        // Collapses \r\n and lone \r to \n so an assertion ignores only the XML-normalized newline STYLE.
        private static string NormalizeNewlines(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static void AssertInt(int expected, int actual, string field)
        {
            if (expected != actual)
            {
                throw new AssertionException(
                    field + " did not survive the Scribe round-trip. expected=" + expected + " actual=" + actual + ".");
            }
        }

        private static void AssertIntList(List<int> expected, List<int> actual, string field)
        {
            Require(actual != null, field + " loaded as a null list.");
            int expectedCount = expected?.Count ?? 0;
            if (actual.Count != expectedCount)
            {
                throw new AssertionException(
                    field + " count changed across the round-trip. expected=" + expectedCount
                    + " actual=" + actual.Count + ".");
            }

            for (int i = 0; i < expectedCount; i++)
            {
                if (expected[i] != actual[i])
                {
                    throw new AssertionException(
                        field + "[" + i + "] changed. expected=" + expected[i] + " actual=" + actual[i] + ".");
                }
            }
        }

        private static void AssertStrList(List<string> expected, List<string> actual, string field)
        {
            Require(actual != null, field + " loaded as a null list.");
            int expectedCount = expected?.Count ?? 0;
            if (actual.Count != expectedCount)
            {
                throw new AssertionException(
                    field + " count changed across the round-trip. expected=" + expectedCount
                    + " actual=" + actual.Count + ".");
            }

            for (int i = 0; i < expectedCount; i++)
            {
                if (!string.Equals(expected[i] ?? string.Empty, actual[i] ?? string.Empty, StringComparison.Ordinal))
                {
                    throw new AssertionException(
                        field + "[" + i + "] changed. expected='" + expected[i] + "' actual='" + actual[i] + "'.");
                }
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertionException(message);
            }
        }
    }
}

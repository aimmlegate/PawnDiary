// Pure save/load normalization fixtures for Plan 6 (Save And Settings Compatibility Fixtures).
//
// These tests pin the post-load repair behavior that DiaryEvent.NormalizeOnLoad and
// ArchivedDiaryEntry.NormalizeOnLoad rely on, without loading RimWorld. The impure steps that
// remain on the save models (Scribe I/O, fresh eventId GUID minting, DefDatabase-backed
// ResolveColorCue, settings clamp/rebuild) are covered by the in-game smoke runbook at
// tests/SAVE_COMPATIBILITY_SMOKETEST.md. Every test below is named after a fixture from the
// Plan 6 fixture inventory, so the regression intent stays readable.
//
// What is NOT here on purpose: Scribe XML round-trip and PawnDiarySettings legacy-field repair.
// Plan 6 Step 4 chose Option B (smoke runbook) for those, because forcing RimWorld Scribe into a
// pure test project would break the no-RimWorld convention every other tests/ project follows.
using System;
using System.Collections.Generic;
using PawnDiary;
using PawnDiary.Capture;

namespace DiarySaveNormalizationTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            // Fixture: "Missing/blank fields repaired by NormalizeOnLoad".
            TestBlankFieldsRepairedToNonNullableDefaults();

            // Fixture: "Pre-title entry" (title follow-up was in-flight when the save was written).
            TestPreTitleEntryClearsStalePendingTitleStatus();

            // Fixture: "Failed entry".
            TestFailedEntryStatusIsPreservedWithoutGeneratedText();

            // Fixture: "Pending archived fallback candidate".
            TestPendingArchivedFallbackCandidateReclassifyAndStaleFlag();

            // Fixture: "Pair event with recipient state".
            TestPairEventRecipientBorrowsInitiatorSurroundings();

            // Fixture: "Neutral arrival" (no saved neutral text; merge both POVs).
            TestNeutralArrivalMergedTextWhenPovsDiffer();
            TestNeutralArrivalMergedTextWhenPovsAgree();

            // Fixture: "Neutral death" — pin the single-copy merge for a shared death description,
            // and pin the exact "name: text\nname: text" shape for divergent POVs.
            TestNeutralDeathSharedDescriptionCollapsesToOneCopy();

            // Fixture: "Legacy settings with old persona/prompt/group fields" — the gameContext and
            // instruction rebuilds are the load-bearing prompt-contract pieces for legacy events.
            TestLegacyEventContextRebuiltInExactShape();

            // Defensive clamps + year extraction (used by both DiaryEvent and ArchivedDiaryEntry).
            TestStaggeredIntensityClampedToZeroThroughFour();
            TestYearExtractedAsLastDigitRunOfDateString();
            TestAnomalySaveKeysAndNewGameState();
            TestAnomalyStateNormalization();
            TestAnomalyLegacyBaseline();

            Console.WriteLine("DiarySaveNormalizationTests passed " + assertions + " assertions.");
            return 0;
        }

        // ------------------------------------------------------------------------------------------
        // Fixture: missing/blank fields repaired by NormalizeOnLoad.
        // ------------------------------------------------------------------------------------------

        private static void TestBlankFieldsRepairedToNonNullableDefaults()
        {
            AssertEqual("null string -> empty", string.Empty, DiarySaveNormalization.NormalizeString(null));
            AssertEqual("non-null string kept", "camp", DiarySaveNormalization.NormalizeString("camp"));

            AssertEqual("whitespace pawn summary -> unknown sentinel",
                DiarySaveNormalization.DefaultPawnSummary,
                DiarySaveNormalization.NormalizeWhitespaceOrDefault(null, DiarySaveNormalization.DefaultPawnSummary));
            AssertEqual("blank continuity -> none sentinel",
                DiarySaveNormalization.DefaultContinuity,
                DiarySaveNormalization.NormalizeWhitespaceOrDefault("   ", DiarySaveNormalization.DefaultContinuity));
            AssertEqual("non-blank value is not trimmed or replaced",
                "by the river",
                DiarySaveNormalization.NormalizeWhitespaceOrDefault("by the river", "unknown"));

            // Empty mood-impact direction defaults to the neutral token, NOT "" — the prompt/UI switch
            // on MoodImpact.Positive/Negative/Neutral must always have a match.
            AssertEqual("default mood impact is neutral",
                "neutral", DiarySaveNormalization.DefaultMoodImpact);
        }

        // ------------------------------------------------------------------------------------------
        // Fixture: pre-title entry. A page whose title follow-up was queued (status "pending") when
        // the save was written must not reload showing an in-flight title. The title status is the
        // piece that gets normalized; the pure logic lives on DiaryGenerationStatus.
        // ------------------------------------------------------------------------------------------

        private static void TestPreTitleEntryClearsStalePendingTitleStatus()
        {
            // Stale pending + no title yet -> cleared so the UI does not say "writing title...".
            AssertEqual("stale pending title -> empty (re-queueable)",
                string.Empty,
                DiaryGenerationStatus.NormalizeLoadedTitleStatus(DiaryGenerationStatus.Pending, string.Empty));

            // If a title actually arrived before the save, the page is complete regardless of status.
            AssertEqual("pending title with text present -> complete",
                DiaryGenerationStatus.Complete,
                DiaryGenerationStatus.NormalizeLoadedTitleStatus(DiaryGenerationStatus.Pending, "First contact"));

            // A title that intentionally failed must stay failed so the recovery sweep does not retry.
            AssertEqual("failed title stays failed",
                DiaryGenerationStatus.Failed,
                DiaryGenerationStatus.NormalizeLoadedTitleStatus(DiaryGenerationStatus.Failed, string.Empty));
        }

        // ------------------------------------------------------------------------------------------
        // Fixture: failed entry. A page that permanently failed must keep its failed status on reload
        // (so it is not retried every scan), unless generated text somehow exists.
        // ------------------------------------------------------------------------------------------

        private static void TestFailedEntryStatusIsPreservedWithoutGeneratedText()
        {
            AssertEqual("failed main entry without text stays failed",
                DiaryGenerationStatus.Failed,
                DiaryGenerationStatus.NormalizeLoadedMainStatus(DiaryGenerationStatus.Failed, string.Empty));

            AssertEqual("failed main entry with stray text upgrades to complete",
                DiaryGenerationStatus.Complete,
                DiaryGenerationStatus.NormalizeLoadedMainStatus(DiaryGenerationStatus.Failed, "We fought off the raid."));

            AssertEqual("skipped main entry stays skipped (not retried)",
                DiaryGenerationStatus.Skipped,
                DiaryGenerationStatus.NormalizeLoadedMainStatus(DiaryGenerationStatus.Skipped, string.Empty));
        }

        // ------------------------------------------------------------------------------------------
        // Fixture: pending archived fallback candidate. A page that was mid-generation when saved
        // reloads as not_generated (the HTTP work is gone), and — if a prompt proves it was actually
        // attempted — is eligible to render as a stale archive fallback instead of disappearing.
        // ------------------------------------------------------------------------------------------

        private static void TestPendingArchivedFallbackCandidateReclassifyAndStaleFlag()
        {
            // Pending + no text reloads as not_generated so the scan re-queues it.
            AssertEqual("pending main entry without text -> not_generated",
                DiaryGenerationStatus.NotGenerated,
                DiaryGenerationStatus.NormalizeLoadedMainStatus(DiaryGenerationStatus.Pending, string.Empty));

            // With a saved prompt, that not_generated page is provably "was attempted", so it is a
            // legitimate stale archive fallback candidate.
            AssertTrue("not_generated + prompt -> archived-stale (fallback candidate)",
                DiaryGenerationStatus.IsArchivedGenerationStale(
                    archivedForScans: true,
                    status: DiaryGenerationStatus.NotGenerated,
                    generatedText: string.Empty,
                    prompt: "event: social fight\nwhat happened: Alice hit Bob."));

            // Without a prompt, the same status must NOT be marked stale — it may be a never-queued
            // raw event that should stay hidden, not a failed attempt.
            AssertTrue("not_generated without prompt -> not stale (no proof of attempt)",
                !DiaryGenerationStatus.IsArchivedGenerationStale(
                    archivedForScans: true,
                    status: DiaryGenerationStatus.NotGenerated,
                    generatedText: string.Empty,
                    prompt: string.Empty));

            // A pending page is stale regardless (the in-flight request is gone but it was attempted).
            AssertTrue("pending page -> stale archive candidate",
                DiaryGenerationStatus.IsArchivedGenerationStale(
                    archivedForScans: true,
                    status: DiaryGenerationStatus.Pending,
                    generatedText: string.Empty,
                    prompt: "event: romance"));
        }

        // ------------------------------------------------------------------------------------------
        // Fixture: pair event with recipient state. The recipient's surroundings borrow the
        // initiator's already-normalized value when blank, so both POVs of a pair event share
        // surroundings unless the recipient explicitly had its own.
        // ------------------------------------------------------------------------------------------

        private static void TestPairEventRecipientBorrowsInitiatorSurroundings()
        {
            string initiator;
            string recipient;

            // Both blank: initiator defaults to the sentinel; recipient borrows that sentinel.
            DiarySaveNormalization.ResolveSurroundingsChain(null, null, out initiator, out recipient);
            AssertEqual("both blank -> initiator unknown", "unknown", initiator);
            AssertEqual("both blank -> recipient borrows unknown", "unknown", recipient);

            // Initiator captured, recipient blank: recipient shares the initiator's value.
            DiarySaveNormalization.ResolveSurroundingsChain("dining room", "", out initiator, out recipient);
            AssertEqual("initiator kept when set", "dining room", initiator);
            AssertEqual("blank recipient borrows initiator", "dining room", recipient);

            // Both captured: both kept exactly.
            DiarySaveNormalization.ResolveSurroundingsChain("dining room", "courtyard", out initiator, out recipient);
            AssertEqual("initiator kept when both set", "dining room", initiator);
            AssertEqual("recipient kept when explicitly set", "courtyard", recipient);

            // Recipient captured, initiator blank: initiator falls back to sentinel; recipient keeps its own.
            DiarySaveNormalization.ResolveSurroundingsChain("", "prison cell", out initiator, out recipient);
            AssertEqual("blank initiator -> unknown even if recipient set", "unknown", initiator);
            AssertEqual("recipient keeps its own when initiator blank", "prison cell", recipient);
        }

        // ------------------------------------------------------------------------------------------
        // Fixture: neutral arrival. A pair event with no saved neutral chronicle text rebuilds it by
        // merging the two POVs. Divergent POVs become "name: text\nname: text"; the delimiter and
        // name-prefix shape are part of the prompt/preview contract.
        // ------------------------------------------------------------------------------------------

        private static void TestNeutralArrivalMergedTextWhenPovsDiffer()
        {
            // Divergent POV texts merge into a single two-line chronicle.
            AssertEqual("divergent POV texts merge with name prefixes",
                "Alice: greeted Bob\nBob: waved back",
                DiarySaveNormalization.BuildDefaultNeutralText(
                    currentNeutralText: string.Empty,
                    initiatorName: "Alice",
                    initiatorText: "greeted Bob",
                    recipientName: "Bob",
                    recipientText: "waved back"));

            // A saved neutral text is always preserved, even when blank-looking whitespace would
            // otherwise trigger a rebuild — the helper treats only null/whitespace as "missing".
            AssertEqual("existing neutral text preserved unchanged",
                "Stored chronicle line.",
                DiarySaveNormalization.BuildDefaultNeutralText(
                    currentNeutralText: "Stored chronicle line.",
                    initiatorName: "Alice",
                    initiatorText: "ignored",
                    recipientName: "Bob",
                    recipientText: "ignored"));

            // Null names (pre-name save) must not throw or produce a literal "null" token.
            AssertEqual("null names coalesce to empty prefix",
                ": hi\n: hello",
                DiarySaveNormalization.BuildDefaultNeutralText(
                    currentNeutralText: null,
                    initiatorName: null,
                    initiatorText: "hi",
                    recipientName: null,
                    recipientText: "hello"));
        }

        private static void TestNeutralArrivalMergedTextWhenPovsAgree()
        {
            // When both POVs saw the same raw text, the merge collapses to a single copy instead of
            // a duplicated two-line page.
            AssertEqual("agreed POV texts collapse to one copy",
                "arrived at the colony",
                DiarySaveNormalization.BuildDefaultNeutralText(
                    currentNeutralText: string.Empty,
                    initiatorName: "Alice",
                    initiatorText: "arrived at the colony",
                    recipientName: "Bob",
                    recipientText: "arrived at the colony"));

            // Ordinal-ignore-case agreement also collapses, matching the production comparison.
            AssertEqual("case-only difference still collapses",
                "Arrived",
                DiarySaveNormalization.BuildDefaultNeutralText(
                    currentNeutralText: " ",
                    initiatorName: "Alice",
                    initiatorText: "Arrived",
                    recipientName: "Bob",
                    recipientText: "ARRIVED"));
        }

        // ------------------------------------------------------------------------------------------
        // Fixture: neutral death. Same merge path as arrival; this pins the exact contract for the
        // death-description case so a future refactor cannot quietly change how a shared death line
        // is rendered on the neutral chronicle page.
        // ------------------------------------------------------------------------------------------

        private static void TestNeutralDeathSharedDescriptionCollapsesToOneCopy()
        {
            AssertEqual("shared death description collapses to one copy",
                "died from blood loss",
                DiarySaveNormalization.BuildDefaultNeutralText(
                    currentNeutralText: null,
                    initiatorName: "Alice",
                    initiatorText: "died from blood loss",
                    recipientName: "witness",
                    recipientText: "died from blood loss"));

            AssertEqual("divergent death/ witnessing texts keep both lines",
                "Alice: died from blood loss\nwitness: saw Alice fall",
                DiarySaveNormalization.BuildDefaultNeutralText(
                    currentNeutralText: null,
                    initiatorName: "Alice",
                    initiatorText: "died from blood loss",
                    recipientName: "witness",
                    recipientText: "saw Alice fall"));
        }

        // ------------------------------------------------------------------------------------------
        // Fixture: legacy event context rebuild. Older saves predate gameContext/instruction fields;
        // the exact "def=...; label=..." shape is re-parsed by DiaryContextFields and embedded in the
        // prompt, so drifting it would shift prompt content for every legacy save on first load.
        // ------------------------------------------------------------------------------------------

        private static void TestLegacyEventContextRebuiltInExactShape()
        {
            AssertEqual("gameContext rebuilt in def/label shape",
                "def=RomanceAttempt; label=Romance attempt",
                DiarySaveNormalization.BuildDefaultGameContext("RomanceAttempt", "Romance attempt"));

            // Null defName/label (pre-field save) still produce a parseable, non-null shape.
            AssertEqual("null fields still produce a parseable context",
                "def=; label=",
                DiarySaveNormalization.BuildDefaultGameContext(null, null));

            // Instruction falls back to the interaction label verbatim; empty when no label was saved.
            AssertEqual("instruction falls back to label",
                "Romance attempt",
                DiarySaveNormalization.BuildDefaultInstruction("Romance attempt"));
            AssertEqual("instruction empty when label missing",
                string.Empty,
                DiarySaveNormalization.BuildDefaultInstruction(null));
        }

        // ------------------------------------------------------------------------------------------
        // Defensive clamps used by both save models.
        // ------------------------------------------------------------------------------------------

        private static void TestStaggeredIntensityClampedToZeroThroughFour()
        {
            AssertEqual("negative -> 0", 0, DiarySaveNormalization.ClampStaggeredIntensity(-5));
            AssertEqual("zero stays zero", 0, DiarySaveNormalization.ClampStaggeredIntensity(0));
            AssertEqual("in-range value passes through", 2, DiarySaveNormalization.ClampStaggeredIntensity(2));
            AssertEqual("max value passes through", 4, DiarySaveNormalization.ClampStaggeredIntensity(4));
            AssertEqual("above max clamps to max", 4, DiarySaveNormalization.ClampStaggeredIntensity(9));
        }

        private static void TestYearExtractedAsLastDigitRunOfDateString()
        {
            AssertEqual("full RimWorld date -> year",
                5502, DiarySaveNormalization.ExtractYear("10 Spring 5502"));
            AssertEqual("small year still parsed",
                3, DiarySaveNormalization.ExtractYear("Summer 3"));
            AssertEqual("last digit run wins when multiple present",
                5501, DiarySaveNormalization.ExtractYear("1 Jan 5500 and 5501"));

            // Missing/empty year is the sentinel, not 0, so "no year" is distinguishable from year 0.
            AssertEqual("blank date -> unknown year sentinel",
                DiarySaveNormalization.UnknownYear, DiarySaveNormalization.ExtractYear(string.Empty));
            AssertEqual("no digits -> unknown year sentinel",
                DiarySaveNormalization.UnknownYear, DiarySaveNormalization.ExtractYear("Late Spring"));
            AssertEqual("whitespace date -> unknown year sentinel",
                DiarySaveNormalization.UnknownYear, DiarySaveNormalization.ExtractYear("   "));
        }

        private static void TestAnomalySaveKeysAndNewGameState()
        {
            AssertEqual("Anomaly schema key", "anomalySupportSchemaVersion",
                AnomalySaveKeys.SupportSchemaVersion);
            AssertEqual("Anomaly first-study key", "anomalyFirstStudyBreakthroughObserved",
                AnomalySaveKeys.FirstStudyBreakthroughObserved);
            AssertEqual("Anomaly completed-study key", "anomalyCompletedStudyDefNames",
                AnomalySaveKeys.CompletedStudyDefNames);
            AssertEqual("Anomaly promotion key", "anomalyPromotedStudyMilestoneKeys",
                AnomalySaveKeys.PromotedStudyMilestoneKeys);
            AssertEqual("Anomaly monolith baseline key", "anomalyMonolithBaselineLevelDefName",
                AnomalySaveKeys.MonolithBaselineLevelDefName);
            AssertEqual("Anomaly monolith snapshot key", "anomalyLastMonolithKnowledgeSnapshot",
                AnomalySaveKeys.LastMonolithKnowledgeSnapshot);

            AnomalyPersistentStateSnapshot fresh = AnomalyPersistencePolicy.NewGame(" Stirring ");
            AssertEqual("new game uses current Anomaly schema",
                AnomalyPersistencePolicy.CurrentSchemaVersion, fresh.schemaVersion);
            AssertTrue("new game has trustworthy unobserved first",
                !fresh.firstStudyBreakthroughObserved);
            AssertEqual("new game trims current monolith level", "Stirring",
                fresh.monolithBaselineLevelDefName);
            AssertEqual("new game has no completed kinds", 0, fresh.completedStudyDefNames.Count);
            AssertTrue("new game invents no monolith knowledge", fresh.lastMonolithKnowledgeSnapshot == null);
        }

        private static void TestAnomalyStateNormalization()
        {
            AnomalyPersistentStateSnapshot raw = new AnomalyPersistentStateSnapshot
            {
                schemaVersion = -4,
                firstStudyBreakthroughObserved = true,
                completedStudyDefNames = new List<string>
                {
                    " Entity_B ", "Entity_A", "Entity_A", "bad|entity", "bad=entity", null
                },
                promotedStudyMilestoneKeys = new List<string>
                {
                    " Entity_B|20|late_notes ", "Entity_A|10|early_notes",
                    "Entity_A|10|early_notes", "bad", "Entity_A|0|zero"
                },
                monolithBaselineLevelDefName = " Waking ",
                lastMonolithKnowledgeSnapshot = new AnomalyMonolithKnowledgeSnapshot
                {
                    researcherPawnId = " Pawn_A ",
                    studyStage = AnomalyStudyStageTokens.Promoted,
                    tick = 100,
                    reachedProgress = 40,
                    becameActivatable = true
                }
            };

            AnomalyPersistentStateSnapshot normalized = AnomalyPersistencePolicy.Normalize(raw);
            AssertEqual("negative Anomaly schema normalizes to legacy", 0, normalized.schemaVersion);
            AssertTrue("Anomaly first-history flag survives", normalized.firstStudyBreakthroughObserved);
            AssertEqual("Anomaly completed kinds deduplicate and filter", 2,
                normalized.completedStudyDefNames.Count);
            AssertEqual("Anomaly completed kinds sort deterministically", "Entity_A",
                normalized.completedStudyDefNames[0]);
            AssertEqual("Anomaly promotion keys deduplicate and filter", 2,
                normalized.promotedStudyMilestoneKeys.Count);
            AssertEqual("Anomaly baseline level trims", "Waking",
                normalized.monolithBaselineLevelDefName);
            AssertTrue("Anomaly monolith snapshot detaches", !object.ReferenceEquals(
                raw.lastMonolithKnowledgeSnapshot, normalized.lastMonolithKnowledgeSnapshot));
            AssertEqual("Anomaly researcher ID trims", "Pawn_A",
                normalized.lastMonolithKnowledgeSnapshot.researcherPawnId);
            AssertEqual("Anomaly monolith progress survives", 40,
                normalized.lastMonolithKnowledgeSnapshot.reachedProgress);

            raw.lastMonolithKnowledgeSnapshot.studyStage = "hidden_downside";
            raw.lastMonolithKnowledgeSnapshot.becameActivatable = false;
            AssertTrue("unknown monolith stage without activation proof drops",
                AnomalyPersistencePolicy.Normalize(raw).lastMonolithKnowledgeSnapshot == null);
            raw.lastMonolithKnowledgeSnapshot.tick = -1;
            raw.lastMonolithKnowledgeSnapshot.becameActivatable = true;
            AssertTrue("negative monolith tick drops",
                AnomalyPersistencePolicy.Normalize(raw).lastMonolithKnowledgeSnapshot == null);

            AnomalyPersistentStateSnapshot future = new AnomalyPersistentStateSnapshot
            {
                schemaVersion = 7
            };
            AssertEqual("future Anomaly schema is not downgraded", 7,
                AnomalyPersistencePolicy.Normalize(future).schemaVersion);

            string oversizedIdentity = new string('X', 201);
            AnomalyPersistentStateSnapshot oversized = AnomalyPersistencePolicy.Normalize(
                new AnomalyPersistentStateSnapshot
                {
                    completedStudyDefNames = new List<string> { oversizedIdentity },
                    promotedStudyMilestoneKeys = new List<string>
                    {
                        "Entity|10|" + oversizedIdentity
                    },
                    monolithBaselineLevelDefName = oversizedIdentity,
                    lastMonolithKnowledgeSnapshot = new AnomalyMonolithKnowledgeSnapshot
                    {
                        researcherPawnId = oversizedIdentity,
                        studyStage = AnomalyStudyStageTokens.FirstBreakthrough,
                        tick = 100,
                        reachedProgress = 10
                    }
                });
            AssertEqual("oversized completed-study identity drops atomically", 0,
                oversized.completedStudyDefNames.Count);
            AssertEqual("oversized promotion identity drops atomically", 0,
                oversized.promotedStudyMilestoneKeys.Count);
            AssertEqual("oversized monolith level does not become a truncated identity", string.Empty,
                oversized.monolithBaselineLevelDefName);
            AssertTrue("oversized researcher identity drops the knowledge snapshot",
                oversized.lastMonolithKnowledgeSnapshot == null);

            List<string> malformedHistory = new List<string>();
            for (int i = 0; i < AnomalyPersistencePolicy.MaximumHistoryRows; i++)
                malformedHistory.Add("bad|row");
            malformedHistory.Add("Entity_AfterDefensiveCap");
            AnomalyPersistentStateSnapshot boundedMalformed =
                AnomalyPersistencePolicy.Normalize(new AnomalyPersistentStateSnapshot
                {
                    completedStudyDefNames = malformedHistory
                });
            AssertEqual("malformed Anomaly history inspection stops at the defensive input cap", 0,
                boundedMalformed.completedStudyDefNames.Count);
        }

        private static void TestAnomalyLegacyBaseline()
        {
            AnomalyPersistentStateSnapshot legacy = new AnomalyPersistentStateSnapshot
            {
                completedStudyDefNames = new List<string> { "Entity_Existing" },
                lastMonolithKnowledgeSnapshot = new AnomalyMonolithKnowledgeSnapshot
                {
                    researcherPawnId = "Pawn_Stale",
                    studyStage = AnomalyStudyStageTokens.FirstBreakthrough,
                    tick = 20,
                    reachedProgress = 10
                }
            };
            AnomalyPersistentStateSnapshot unavailable = AnomalyPersistencePolicy.BaselineLegacy(
                legacy,
                new AnomalyLegacyBaselineFacts { anomalyAvailable = false });
            AssertEqual("Anomaly-inactive legacy save remains pending", 0, unavailable.schemaVersion);
            AssertTrue("Anomaly-inactive legacy save does not invent first history",
                !unavailable.firstStudyBreakthroughObserved);

            AnomalyPersistentStateSnapshot baseline = AnomalyPersistencePolicy.BaselineLegacy(
                legacy,
                new AnomalyLegacyBaselineFacts
                {
                    anomalyAvailable = true,
                    historyComplete = false,
                    anyCommittedStudyProgress = false,
                    currentMonolithLevelDefName = "VoidAwakened",
                    completedStudyDefNames = new List<string>
                    {
                        "Entity_New", "Entity_Existing", "bad|entity"
                    }
                });
            AssertEqual("active legacy baseline advances schema",
                AnomalyPersistencePolicy.CurrentSchemaVersion, baseline.schemaVersion);
            AssertTrue("incomplete old-save history suppresses false first",
                baseline.firstStudyBreakthroughObserved);
            AssertEqual("old-save completed kinds merge and deduplicate", 2,
                baseline.completedStudyDefNames.Count);
            AssertEqual("old-save monolith level baselines", "VoidAwakened",
                baseline.monolithBaselineLevelDefName);
            AssertTrue("old-save baseline discards unprovable knowledge ownership",
                baseline.lastMonolithKnowledgeSnapshot == null);

            AnomalyPersistentStateSnapshot completeEmpty = AnomalyPersistencePolicy.BaselineLegacy(
                new AnomalyPersistentStateSnapshot(),
                new AnomalyLegacyBaselineFacts
                {
                    anomalyAvailable = true,
                    historyComplete = true,
                    anyCommittedStudyProgress = false
                });
            AssertTrue("provably empty legacy history may retain a future genuine first",
                !completeEmpty.firstStudyBreakthroughObserved);

            AnomalyPersistentStateSnapshot current = new AnomalyPersistentStateSnapshot
            {
                schemaVersion = AnomalyPersistencePolicy.CurrentSchemaVersion
            };
            AnomalyPersistentStateSnapshot unchanged = AnomalyPersistencePolicy.BaselineLegacy(
                current,
                new AnomalyLegacyBaselineFacts
                {
                    anomalyAvailable = true,
                    historyComplete = false,
                    currentMonolithLevelDefName = "Embraced"
                });
            AssertTrue("current schema does not rebaseline or manufacture first",
                !unchanged.firstStudyBreakthroughObserved);
            AssertEqual("current schema keeps its existing monolith baseline", string.Empty,
                unchanged.monolithBaselineLevelDefName);
        }

        // ------------------------------------------------------------------------------------------
        // Minimal assert helpers (match the convention in the other tests/ projects).
        // ------------------------------------------------------------------------------------------

        private static void AssertEqual(string label, string expected, string actual)
        {
            assertions++;
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "ASSERT FAILED [" + label + "]: expected <" + expected + "> actual <" + actual + ">");
            }
        }

        private static void AssertEqual(string label, int expected, int actual)
        {
            assertions++;
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    "ASSERT FAILED [" + label + "]: expected <" + expected + "> actual <" + actual + ">");
            }
        }

        private static void AssertTrue(string label, bool condition)
        {
            assertions++;
            if (!condition)
            {
                throw new InvalidOperationException("ASSERT FAILED [" + label + "]: expected true");
            }
        }
    }
}

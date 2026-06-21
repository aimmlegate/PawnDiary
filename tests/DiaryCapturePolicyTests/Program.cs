// Pure unit tests for the Event Catalog decision layer. These exercise ThoughtEventData.Decide and
// InspirationEventData.Decide without RimWorld assemblies, plus the DiaryEventCatalog dispatch. Run
// via: build DiaryCapturePolicyTests.csproj, then execute the resulting exe (exit code 0 = pass).
using System;
using System.Collections.Generic;
using PawnDiary.Capture;

namespace DiaryCapturePolicyTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestThoughtNullGuards();
            TestThoughtEligibilityGates();
            TestThoughtPermanentDrop();
            TestThoughtIgnoreToken();
            TestThoughtGeneralThreshold();
            TestThoughtEatingThreshold();
            TestThoughtBypassToken();
            TestThoughtAmbientRouting();
            TestThoughtPolicyNullDefaults();
            TestThoughtBuildGameContextFormat();
            TestInspirationDecide();
            TestInspirationBuildGameContextFormat();
            TestMoodEventDecide();
            TestMoodEventBuildGameContextFormat();
            TestMentalStateDecide();
            TestMentalStateBuildPairGameContextFormat();
            TestMentalStateBuildSoloGameContextFormat();
            TestTaleDecide();
            TestTaleBuildGameContextFormat();
            TestHediffDecide();
            TestHediffBuildGameContextFormat();
            TestInteractionDecide();
            TestInteractionBuildGameContextFormat();
            TestRomanceDecide();
            TestRomanceBuildGameContextFormat();
            TestRomanceKindFor();
            TestCatalogDispatch();
            TestCatalogContract();
            TestMigrationSentinel();

            Console.WriteLine("DiaryCapturePolicyTests passed " + assertions + " assertions.");
            return 0;
        }

        // ── Thought: guards and gates ──

        private static void TestThoughtNullGuards()
        {
            AssertEqual("null data drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(null, Ctx()));
            AssertEqual("null ctx drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Any", mood: 10f), null));
        }

        private static void TestThoughtEligibilityGates()
        {
            AssertEqual("ineligible drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: 10f), Ctx(eligible: false)));
            AssertEqual("signal disabled drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: 10f), Ctx(signal: false)));
            AssertEqual("user disabled drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: 10f), Ctx(user: false)));
            AssertEqual("all enabled records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("Foo", mood: 10f), Ctx()));
        }

        // ── Thought: source-specific filters ──

        private static void TestThoughtPermanentDrop()
        {
            AssertEqual("duration zero drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: 10f, duration: 0f), Ctx()));
            AssertEqual("duration negative drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: 10f, duration: -1f), Ctx()));
        }

        private static void TestThoughtIgnoreToken()
        {
            // Default policy ignore token is "Insult" (substring, case-insensitive).
            AssertEqual("ignore token match drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Insulted", mood: 10f), Ctx()));
            // Default policy does NOT include "Foo", so a "Foo" thought is not ignored — every other
            // passing test already proves that path returns GenerateSolo.
        }

        private static void TestThoughtGeneralThreshold()
        {
            // Default general bar is +/-5.
            AssertEqual("below general threshold drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: 4.9f), Ctx()));
            AssertEqual("negative below threshold drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Foo", mood: -4.9f), Ctx()));
            AssertEqual("at general threshold records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("Foo", mood: 5f), Ctx()));
            AssertEqual("above general threshold records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("Foo", mood: -10f), Ctx()));
        }

        private static void TestThoughtEatingThreshold()
        {
            // Default eating bar is +/-15; eating token is "Ate".
            AssertEqual("eating below eating-bar drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("AteWithoutTable", mood: 14f), Ctx()));
            AssertEqual("eating above eating-bar records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("AteFineMeal", mood: 16f), Ctx()));
            AssertEqual("eating at eating-bar records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("AteGood", mood: 15f), Ctx()));
        }

        private static void TestThoughtBypassToken()
        {
            // Bypass token is "Death" — magnitude threshold is skipped entirely. defName must
            // actually contain the token (substring match), so we use DeathOfFriend here.
            AssertEqual("bypass with low magnitude records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("DeathOfFriend", mood: 0.1f), Ctx()));
            AssertEqual("bypass with zero magnitude records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("DeathOfFriend", mood: 0f), Ctx()));
        }

        private static void TestThoughtAmbientRouting()
        {
            // Ambient token is "Nuzzle".
            AssertEqual("ambient token + ambient enabled routes", CaptureDecision.RouteAmbient,
                ThoughtEventData.Decide(Thought("Nuzzled", mood: 6f), Ctx(ambient: true)));
            // Same thought but ambient disabled → still recorded as solo (ambient is optional routing,
            // not a filter; if ambient is off the thought still qualifies as a normal solo event).
            AssertEqual("ambient token + ambient disabled records solo", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(Thought("Nuzzled", mood: 6f), Ctx(ambient: false)));
            // Ambient token below general threshold still drops: ambient routing only runs after the
            // magnitude gate, matching pre-refactor order.
            AssertEqual("ambient below threshold drops", CaptureDecision.Drop,
                ThoughtEventData.Decide(Thought("Nuzzled", mood: 1f), Ctx(ambient: true)));
        }

        private static void TestThoughtPolicyNullDefaults()
        {
            // A null policy means "no tokens, no thresholds" — every eligible expiring thought records.
            ThoughtEventData data = new ThoughtEventData
            {
                PawnId = "P",
                Tick = 0,
                DefName = "Anything",
                MoodOffset = 0f,
                DurationDays = 1f,
                MoodImpact = MoodImpact("neutral"),
                Policy = null,
            };
            AssertEqual("null policy records", CaptureDecision.GenerateSolo,
                ThoughtEventData.Decide(data, Ctx()));
        }

        private static void TestThoughtBuildGameContextFormat()
        {
            // The leading "thought=" marker and the field order/precision are load-bearing: the UI
            // parses the marker to recover the Thought domain, and the LLM reads the rest. Locking the
            // format here means a future migration cannot silently drift it.
            string ctx = ThoughtEventData.BuildGameContext(
                "AteWithoutTable", "ate without table", "negative", -5.27f, 0.4f);
            AssertEqual("thought context full format",
                "thought=AteWithoutTable; label=ate without table; mood_impact=negative; mood_offset=-5.3; duration_days=0.4",
                ctx);

            // Positive mood impact, integer-valued offset formats with one decimal.
            string positive = ThoughtEventData.BuildGameContext(
                "GotMarried", "got married", "positive", 12f, 2f);
            AssertEqual("thought context positive integer floats",
                "thought=GotMarried; label=got married; mood_impact=positive; mood_offset=12.0; duration_days=2.0",
                positive);

            // Neutral impact passes through verbatim — classification happens in the caller, the build
            // helper just embeds whatever it receives.
            string neutral = ThoughtEventData.BuildGameContext(
                "Foo", "bar", "neutral", 0f, 1f);
            AssertEqual("thought context neutral impact",
                "thought=Foo; label=bar; mood_impact=neutral; mood_offset=0.0; duration_days=1.0",
                neutral);
        }

        // ── Inspiration ──

        private static void TestInspirationDecide()
        {
            AssertEqual("inspiration null data drops", CaptureDecision.Drop,
                InspirationEventData.Decide(null, Ctx()));
            AssertEqual("inspiration null ctx drops", CaptureDecision.Drop,
                InspirationEventData.Decide(Inspiration("Foo"), null));
            AssertEqual("inspiration ineligible drops", CaptureDecision.Drop,
                InspirationEventData.Decide(Inspiration("Foo"), Ctx(eligible: false)));
            AssertEqual("inspiration user disabled drops", CaptureDecision.Drop,
                InspirationEventData.Decide(Inspiration("Foo"), Ctx(user: false)));
            AssertEqual("inspiration eligible records", CaptureDecision.GenerateSolo,
                InspirationEventData.Decide(Inspiration("Inspired_Recruitment"), Ctx()));
        }

        private static void TestInspirationBuildGameContextFormat()
        {
            // The leading "inspiration=" marker is load-bearing for UI domain classification.
            // With a reason: the optional "reason=" field is appended.
            string withReason = InspirationEventData.BuildGameContext(
                "Inspired_Recruitment", "recruitment drive", 8.0f, "felt inspired");
            AssertEqual("inspiration context with reason",
                "inspiration=Inspired_Recruitment; label=recruitment drive; duration_days=8.0; reason=felt inspired",
                withReason);

            // Without a reason: the field is omitted entirely (not emitted as empty), matching
            // pre-refactor behavior.
            string noReason = InspirationEventData.BuildGameContext(
                "Inspired_Creativity", "creative inspiration", 8.5f, "");
            AssertEqual("inspiration context without reason",
                "inspiration=Inspired_Creativity; label=creative inspiration; duration_days=8.5",
                noReason);

            // Whitespace-only reason is also treated as absent (the helper guards against it).
            string whitespaceReason = InspirationEventData.BuildGameContext(
                "Inspired_Trade", "trade deal", 8.0f, "   ");
            AssertEqual("inspiration context whitespace reason omitted",
                "inspiration=Inspired_Trade; label=trade deal; duration_days=8.0",
                whitespaceReason);
        }

        // ── MoodEvent ──

        private static void TestMoodEventDecide()
        {
            // MoodEvent has multi-pawn fan-out, but the catalog sees one event at a time — so the
            // Decider is the same trivial shape as Inspiration: eligibility + user toggle only.
            AssertEqual("mood event null data drops", CaptureDecision.Drop,
                MoodEventData.Decide(null, Ctx()));
            AssertEqual("mood event null ctx drops", CaptureDecision.Drop,
                MoodEventData.Decide(MoodEvent("Aurora"), null));
            AssertEqual("mood event ineligible drops", CaptureDecision.Drop,
                MoodEventData.Decide(MoodEvent("Aurora"), Ctx(eligible: false)));
            AssertEqual("mood event user disabled drops", CaptureDecision.Drop,
                MoodEventData.Decide(MoodEvent("Aurora"), Ctx(user: false)));
            AssertEqual("mood event eligible records", CaptureDecision.GenerateSolo,
                MoodEventData.Decide(MoodEvent("PsychicDrone"), Ctx()));
        }

        private static void TestMoodEventBuildGameContextFormat()
        {
            // The leading "mood_event=" marker is load-bearing for UI domain classification. The
            // format has no numeric fields (unlike Thought/Inspiration), so the test focuses on
            // marker position, field order, and mood-impact token pass-through.
            string positive = MoodEventData.BuildGameContext("Aurora", "aurora", "positive");
            AssertEqual("mood event positive context",
                "mood_event=Aurora; label=aurora; mood_impact=positive",
                positive);

            string negative = MoodEventData.BuildGameContext("PsychicDrone", "psychic drone", "negative");
            AssertEqual("mood event negative context",
                "mood_event=PsychicDrone; label=psychic drone; mood_impact=negative",
                negative);

            string neutral = MoodEventData.BuildGameContext("Eclipse", "eclipse", "neutral");
            AssertEqual("mood event neutral context",
                "mood_event=Eclipse; label=eclipse; mood_impact=neutral",
                neutral);
        }

        // ── MentalState (first pair source) ──

        private static void TestMentalStateDecide()
        {
            // Null guards.
            AssertEqual("mental state null data drops", CaptureDecision.Drop,
                MentalStateEventData.Decide(null, Ctx()));
            AssertEqual("mental state null ctx drops", CaptureDecision.Drop,
                MentalStateEventData.Decide(MentalState("Berserk"), null));

            // Eligibility / user toggle gates.
            AssertEqual("mental state ineligible drops", CaptureDecision.Drop,
                MentalStateEventData.Decide(MentalState("Berserk"), Ctx(eligible: false)));
            AssertEqual("mental state user disabled drops", CaptureDecision.Drop,
                MentalStateEventData.Decide(MentalState("Berserk"), Ctx(user: false)));

            // Solo break: non-SocialFighting defName → GenerateSolo regardless of otherPawn.
            AssertEqual("non-social-fight solo break", CaptureDecision.GenerateSolo,
                MentalStateEventData.Decide(MentalState("Berserk", otherId: "P2"), Ctx()));
            AssertEqual("solo break without other pawn", CaptureDecision.GenerateSolo,
                MentalStateEventData.Decide(MentalState("SadWander"), Ctx()));

            // Pair: SocialFighting + eligible counterpart + different pawn → GeneratePair.
            AssertEqual("social fight pair", CaptureDecision.GeneratePair,
                MentalStateEventData.Decide(
                    MentalState("SocialFighting", otherId: "P2", otherEligible: true),
                    Ctx()));

            // Pair falls back to solo when defName is not SocialFighting.
            AssertEqual("non-social-fight with eligible other stays solo", CaptureDecision.GenerateSolo,
                MentalStateEventData.Decide(
                    MentalState("Berserk", otherId: "P2", otherEligible: true),
                    Ctx()));

            // Pair falls back to solo when counterpart is ineligible.
            AssertEqual("social fight with ineligible counterpart is solo", CaptureDecision.GenerateSolo,
                MentalStateEventData.Decide(
                    MentalState("SocialFighting", otherId: "P2", otherEligible: false),
                    Ctx()));

            // Pair falls back to solo when counterpart == self (degenerate call the hook should
            // never emit; the catalog guards against self-pair anyway).
            AssertEqual("social fight with self counterpart is solo", CaptureDecision.GenerateSolo,
                MentalStateEventData.Decide(
                    MentalState("SocialFighting", otherId: "P", otherEligible: true),
                    Ctx()));

            // Pair falls back to solo when no counterpart provided.
            AssertEqual("social fight without other pawn is solo", CaptureDecision.GenerateSolo,
                MentalStateEventData.Decide(MentalState("SocialFighting"), Ctx()));

            // DefName match is case-insensitive.
            AssertEqual("social fight defName case-insensitive", CaptureDecision.GeneratePair,
                MentalStateEventData.Decide(
                    MentalState("socialfighting", otherId: "P2", otherEligible: true),
                    Ctx()));
        }

        private static void TestMentalStateBuildPairGameContextFormat()
        {
            // Pair context (social fights): no target field (both pawns are POV participants).
            // Reason is optional.
            string withReason = MentalStateEventData.BuildPairGameContext(
                "SocialFighting", "social fight", "they argued over food");
            AssertEqual("mental state pair context with reason",
                "mental_state=SocialFighting; label=social fight; reason=they argued over food",
                withReason);

            string noReason = MentalStateEventData.BuildPairGameContext(
                "SocialFighting", "social fight", "");
            AssertEqual("mental state pair context no reason",
                "mental_state=SocialFighting; label=social fight",
                noReason);

            string whitespaceReason = MentalStateEventData.BuildPairGameContext(
                "SocialFighting", "social fight", "   ");
            AssertEqual("mental state pair context whitespace reason omitted",
                "mental_state=SocialFighting; label=social fight",
                whitespaceReason);
        }

        private static void TestMentalStateBuildSoloGameContextFormat()
        {
            // Solo context (mental breaks): optional target + optional reason.
            string withTargetAndReason = MentalStateEventData.BuildSoloGameContext(
                "Berserk", "berserk rage", "Bob", "slept in the rain");
            AssertEqual("mental state solo context target + reason",
                "mental_state=Berserk; label=berserk rage; target=Bob; reason=slept in the rain",
                withTargetAndReason);

            string targetNoReason = MentalStateEventData.BuildSoloGameContext(
                "InsultSpree", "insult spree", "Alice", "");
            AssertEqual("mental state solo context target only",
                "mental_state=InsultSpree; label=insult spree; target=Alice",
                targetNoReason);

            string noTargetWithReason = MentalStateEventData.BuildSoloGameContext(
                "SadWander", "sad wandering", null, "lost a friend");
            AssertEqual("mental state solo context reason only",
                "mental_state=SadWander; label=sad wandering; reason=lost a friend",
                noTargetWithReason);

            string bareContext = MentalStateEventData.BuildSoloGameContext(
                "ConfusedWander", "confused wandering", null, "");
            AssertEqual("mental state solo context bare",
                "mental_state=ConfusedWander; label=confused wandering",
                bareContext);
        }

        // ── Tale (partial migration — drop-gate only) ──

        private static void TestTaleDecide()
        {
            // Null guards.
            AssertEqual("tale null data drops", CaptureDecision.Drop,
                TaleEventData.Decide(null, Ctx()));
            AssertEqual("tale null ctx drops", CaptureDecision.Drop,
                TaleEventData.Decide(Tale("KilledMan"), null));

            // Covered-elsewhere: defName in TaleEventData.CoveredElsewhere set.
            AssertEqual("tale covered elsewhere drops", CaptureDecision.Drop,
                TaleEventData.Decide(Tale("SocialFight", coveredElsewhere: true), Ctx()));
            AssertEqual("tale covered elsewhere case-insensitive", CaptureDecision.Drop,
                TaleEventData.Decide(Tale("mentalstateberserk", coveredElsewhere: true), Ctx()));

            // GameCondition-duplicate: GameCondition domain (MoodEvent) owns it.
            AssertEqual("tale game-condition duplicate drops", CaptureDecision.Drop,
                TaleEventData.Decide(Tale("Eclipse", gameConditionDup: true), Ctx()));

            // Signal / user gates.
            AssertEqual("tale signal disabled drops", CaptureDecision.Drop,
                TaleEventData.Decide(Tale("KilledMan"), Ctx(signal: false)));
            AssertEqual("tale user disabled drops", CaptureDecision.Drop,
                TaleEventData.Decide(Tale("KilledMan"), Ctx(user: false)));

            // Eligibility: neither participant eligible → Drop.
            AssertEqual("tale no eligible pawns drops", CaptureDecision.Drop,
                TaleEventData.Decide(
                    new TaleEventData { DefName = "KilledMan" },  // FirstEligible/SecondEligible default false
                    Ctx()));

            // Continuation: at least one eligible + none of the gates fire → GenerateSolo
            // (RecordTale re-runs the shape dispatch; see file header TODO).
            AssertEqual("tale first-only eligible continues", CaptureDecision.GenerateSolo,
                TaleEventData.Decide(Tale("KilledMan", firstEligible: true), Ctx()));
            AssertEqual("tale second-only eligible continues", CaptureDecision.GenerateSolo,
                TaleEventData.Decide(Tale("Wounded", secondEligible: true), Ctx()));
            AssertEqual("tale both eligible continues", CaptureDecision.GenerateSolo,
                TaleEventData.Decide(Tale("DidResearch", firstEligible: true, secondEligible: true), Ctx()));
        }

        private static void TestTaleBuildGameContextFormat()
        {
            // Base format: 3 mandatory fields, no attachedDef.
            string baseCtx = TaleEventData.BuildGameContext(
                "KilledMan", "killed a man", "Tale_DoublePawn", null, null);
            AssertEqual("tale base context",
                "tale=KilledMan; label=killed a man; taleClass=Tale_DoublePawn",
                baseCtx);

            // With attached def (research project, skill, etc.) — both fields present.
            string withDef = TaleEventData.BuildGameContext(
                "DidResearch", "finished research", "Tale_SinglePawnAndDef",
                "ResearchProject_Electricity", "Electricity");
            AssertEqual("tale context with attached def",
                "tale=DidResearch; label=finished research; taleClass=Tale_SinglePawnAndDef; attachedDef=ResearchProject_Electricity; attachedLabel=Electricity",
                withDef);

            // attachedDef name present but label empty → only attachedDef field emitted.
            string defNoLabel = TaleEventData.BuildGameContext(
                "CraftedArt", "crafted art", "Tale_SinglePawnAndDef", "Sculpture", "");
            AssertEqual("tale context attached def no label",
                "tale=CraftedArt; label=crafted art; taleClass=Tale_SinglePawnAndDef; attachedDef=Sculpture",
                defNoLabel);
        }

        // ── Hediff (partial migration — drop-gate only) ──

        private static void TestHediffDecide()
        {
            // Null guards.
            AssertEqual("hediff null data drops", CaptureDecision.Drop,
                HediffEventData.Decide(null, Ctx()));
            AssertEqual("hediff null ctx drops", CaptureDecision.Drop,
                HediffEventData.Decide(Hediff(), null));

            // Empty defName → Drop.
            AssertEqual("hediff empty defName drops", CaptureDecision.Drop,
                HediffEventData.Decide(Hediff(defName: ""), Ctx()));

            // Eligibility / user gate.
            AssertEqual("hediff ineligible drops", CaptureDecision.Drop,
                HediffEventData.Decide(Hediff(), Ctx(eligible: false)));
            AssertEqual("hediff user disabled drops", CaptureDecision.Drop,
                HediffEventData.Decide(Hediff(), Ctx(user: false)));

            // Policy gates: PolicyRecordsSource / ModeRecordable / PassesPolicy (any false → Drop).
            AssertEqual("hediff policy not recording source drops", CaptureDecision.Drop,
                HediffEventData.Decide(Hediff(policyRecordsSource: false), Ctx()));
            AssertEqual("hediff mode not recordable drops", CaptureDecision.Drop,
                HediffEventData.Decide(Hediff(modeRecordable: false), Ctx()));
            AssertEqual("hediff fails policy drops", CaptureDecision.Drop,
                HediffEventData.Decide(Hediff(passesPolicy: false), Ctx()));

            // Continuation: all gates pass → GenerateSolo (RecordHediffSignal re-dispatches
            // Immediate vs DayReflection locally; see file header TODO).
            AssertEqual("hediff all gates pass continues", CaptureDecision.GenerateSolo,
                HediffEventData.Decide(Hediff(), Ctx()));
        }

        private static void TestHediffBuildGameContextFormat()
        {
            // Base format: 7 mandatory fields, no stage_label/body_part.
            string baseCtx = HediffEventData.BuildGameContext(
                "Wound", "wound", "add", "hediff_injury", "Immediate", "0.50", "0", null, null);
            AssertEqual("hediff base context",
                "hediff=Wound; label=wound; source=add; group=hediff_injury; mode=Immediate; severity=0.50; stage=0",
                baseCtx);

            // With stage_label and body_part.
            string withExtras = HediffEventData.BuildGameContext(
                "SurgeryComplication", "surgery complication", "severity_progression", "hediff_surgery",
                "DayReflection", "1.20", "2", "infection", "left arm");
            AssertEqual("hediff context with extras",
                "hediff=SurgeryComplication; label=surgery complication; source=severity_progression; group=hediff_surgery; mode=DayReflection; severity=1.20; stage=2; stage_label=infection; body_part=left arm",
                withExtras);

            // Progressed source token + Immediate mode + no extras.
            string progressed = HediffEventData.BuildGameContext(
                "GutWorms", "gut worms", "severity_progression", "hediff_disease", "Immediate",
                "0.75", "1", "", "");
            AssertEqual("hediff progressed context",
                "hediff=GutWorms; label=gut worms; source=severity_progression; group=hediff_disease; mode=Immediate; severity=0.75; stage=1",
                progressed);
        }

        // ── Interaction (partial migration — drop-gate only) ──

        private static void TestInteractionDecide()
        {
            // Null guards.
            AssertEqual("interaction null data drops", CaptureDecision.Drop,
                InteractionEventData.Decide(null, Ctx()));
            AssertEqual("interaction null ctx drops", CaptureDecision.Drop,
                InteractionEventData.Decide(Interaction(), null));

            // Empty defName → Drop.
            AssertEqual("interaction empty defName drops", CaptureDecision.Drop,
                InteractionEventData.Decide(Interaction(defName: ""), Ctx()));

            // Significance gate.
            AssertEqual("interaction not significant drops", CaptureDecision.Drop,
                InteractionEventData.Decide(Interaction(isSignificant: false), Ctx()));

            // User gate.
            AssertEqual("interaction user disabled drops", CaptureDecision.Drop,
                InteractionEventData.Decide(Interaction(), Ctx(user: false)));

            // No eligible pawn → Drop.
            AssertEqual("interaction no eligible pawns drops", CaptureDecision.Drop,
                InteractionEventData.Decide(
                    Interaction(initiatorEligible: false, recipientEligible: false),
                    Ctx()));

            // At least one eligible + significant + enabled → GenerateSolo (continue).
            AssertEqual("interaction initiator only eligible continues", CaptureDecision.GenerateSolo,
                InteractionEventData.Decide(
                    Interaction(initiatorEligible: true, recipientEligible: false), Ctx()));
            AssertEqual("interaction recipient only eligible continues", CaptureDecision.GenerateSolo,
                InteractionEventData.Decide(
                    Interaction(initiatorEligible: false, recipientEligible: true), Ctx()));
            AssertEqual("interaction both eligible continues", CaptureDecision.GenerateSolo,
                InteractionEventData.Decide(Interaction(), Ctx()));
        }

        private static void TestInteractionBuildGameContextFormat()
        {
            // Base format: 2 mandatory fields (def + label).
            string baseCtx = InteractionEventData.BuildGameContext("Insult", "insult", null, null, null);
            AssertEqual("interaction base context",
                "def=Insult; label=insult",
                baseCtx);

            // All optional fields populated.
            string full = InteractionEventData.BuildGameContext(
                "Chat", "chat", "InteractionWorker_Chat", "talked", "listened");
            AssertEqual("interaction full context",
                "def=Chat; label=chat; worker=InteractionWorker_Chat; initiatorThought=talked; recipientThought=listened",
                full);

            // Only worker, no thoughts.
            string workerOnly = InteractionEventData.BuildGameContext(
                "Hug", "hug", "InteractionWorker_Hug", "", "");
            AssertEqual("interaction worker only context",
                "def=Hug; label=hug; worker=InteractionWorker_Hug",
                workerOnly);
        }

        // ── Romance (first net-new source) ──

        private static void TestRomanceDecide()
        {
            // Null guards.
            AssertEqual("romance null data drops", CaptureDecision.Drop,
                RomanceEventData.Decide(null, Ctx()));
            AssertEqual("romance null ctx drops", CaptureDecision.Drop,
                RomanceEventData.Decide(Romance("Lover"), null));

            // Signal / user gates.
            AssertEqual("romance signal disabled drops", CaptureDecision.Drop,
                RomanceEventData.Decide(Romance("Lover"), Ctx(signal: false)));
            AssertEqual("romance user disabled drops", CaptureDecision.Drop,
                RomanceEventData.Decide(Romance("Lover"), Ctx(user: false)));

            // Both pawns must be eligible (a pair event between a colonist and a non-colonist
            // does not diary).
            AssertEqual("romance first ineligible drops", CaptureDecision.Drop,
                RomanceEventData.Decide(Romance("Lover", firstEligible: false), Ctx()));
            AssertEqual("romance second ineligible drops", CaptureDecision.Drop,
                RomanceEventData.Decide(Romance("Lover", secondEligible: false), Ctx()));

            // Degenerate self-relation.
            AssertEqual("romance self-pair drops", CaptureDecision.Drop,
                RomanceEventData.Decide(Romance("Lover", secondId: "P1"), Ctx()));

            // Both eligible + different pawns → GeneratePair.
            AssertEqual("romance both eligible generates pair", CaptureDecision.GeneratePair,
                RomanceEventData.Decide(Romance("Spouse"), Ctx()));
        }

        private static void TestRomanceBuildGameContextFormat()
        {
            // The leading "romance=" marker is load-bearing for UI domain classification; the
            // "kind=" field carries the short human-readable category.
            AssertEqual("romance lover context",
                "romance=Lover; label=lover; kind=lover",
                RomanceEventData.BuildGameContext("Lover", "lover", "lover"));
            AssertEqual("romance spouse context",
                "romance=Spouse; label=spouse; kind=married",
                RomanceEventData.BuildGameContext("Spouse", "spouse", "married"));
            AssertEqual("romance exlover context",
                "romance=ExLover; label=ex-lover; kind=breakup",
                RomanceEventData.BuildGameContext("ExLover", "ex-lover", "breakup"));
            AssertEqual("romance exspouse context",
                "romance=ExSpouse; label=ex-spouse; kind=divorce",
                RomanceEventData.BuildGameContext("ExSpouse", "ex-spouse", "divorce"));
        }

        private static void TestRomanceKindFor()
        {
            // Vanilla relation defNames map to the four kind tokens; modded defNames fall back to
            // the raw defName so the catalog never throws on an unknown relation.
            AssertEqual("kind Spouse", "married", RomanceEventData.KindFor("Spouse"));
            AssertEqual("kind Lover", "lover", RomanceEventData.KindFor("Lover"));
            AssertEqual("kind ExSpouse", "divorce", RomanceEventData.KindFor("ExSpouse"));
            AssertEqual("kind ExLover", "breakup", RomanceEventData.KindFor("ExLover"));
            AssertEqual("kind case-insensitive", "married", RomanceEventData.KindFor("SPOUSE"));
            AssertEqual("kind modded fallback", "Bonded", RomanceEventData.KindFor("Bonded"));
            AssertEqual("kind empty defName", string.Empty, RomanceEventData.KindFor(null));
        }

        // ── Catalog dispatch ──

        private static void TestCatalogDispatch()
        {
            DiaryEventCatalog.Reset();

            DiaryEventSpec thoughtSpec = DiaryEventCatalog.Get(DiaryEventType.Thought);
            AssertTrue("catalog has Thought spec", thoughtSpec is ThoughtEventSpec);
            AssertEqual("catalog dispatches Thought decision",
                CaptureDecision.GenerateSolo,
                thoughtSpec.Decide(Thought("Foo", mood: 10f), Ctx()));

            DiaryEventSpec inspirationSpec = DiaryEventCatalog.Get(DiaryEventType.Inspiration);
            AssertTrue("catalog has Inspiration spec", inspirationSpec is InspirationEventSpec);
            AssertEqual("catalog dispatches Inspiration decision",
                CaptureDecision.GenerateSolo,
                inspirationSpec.Decide(Inspiration("Foo"), Ctx()));

            // Unregistered spec returns null — callers must treat as Drop.
            // (We can't construct a DiaryEventType that isn't registered because all values are
            // registered today, so verify Reset re-registers defaults.)
            AssertTrue("catalog spec is non-null after reset", DiaryEventCatalog.Get(DiaryEventType.Thought) != null);
        }

        // ── Catalog contract (invariants every migration must preserve) ──

        private static void TestCatalogContract()
        {
            DiaryEventCatalog.Reset();

            // (1) Every value declared in DiaryEventType MUST have a registered Spec. Catches the
            // classic "I added the enum entry but forgot Register()" half-finished migration.
            foreach (DiaryEventType type in (DiaryEventType[])Enum.GetValues(typeof(DiaryEventType)))
            {
                DiaryEventSpec spec = DiaryEventCatalog.Get(type);
                AssertTrue("catalog has spec for " + type, spec != null);
                // (2) The spec's own EventType must match the key it is registered under. Catches a
                // spec being registered against the wrong enum value.
                AssertEqual("spec EventType matches key for " + type, type, spec.EventType);
            }

            // (3) Reset is idempotent: re-resetting leaves the catalog in the same shape.
            DiaryEventCatalog.Reset();
            foreach (DiaryEventType type in (DiaryEventType[])Enum.GetValues(typeof(DiaryEventType)))
            {
                AssertTrue("catalog still has spec after re-reset for " + type,
                    DiaryEventCatalog.Get(type) != null);
            }
        }

        // ── Migration sentinel — list of planned sources NOT yet migrated ──
        // Every name in this list is a DiaryEventType we PLAN to add in future slices. The test
        // asserts each one is still ABSENT from the enum. When you migrate a source:
        //   1. Add the value to DiaryEventType.
        //   2. Write XxxEventData + XxxEventSpec + Register.
        //   3. REMOVE its name from this list.
        // The TestCatalogContract step (1) above already enforces "every enum value has a Spec", so
        // this sentinel only protects against accidentally-landed enum values that nobody migrated.
        private static readonly string[] PlannedNotYetMigratedSources =
        {
            "Quest", "Raid", "MajorThreat", "RandomEvent", "WorldEvent",
            "AnomalyEvent", "IncidentEvent", "Health",
            "Arrival", "Death",
        };

        private static void TestMigrationSentinel()
        {
            foreach (string name in PlannedNotYetMigratedSources)
            {
                // Each planned-but-not-yet-migrated source MUST NOT be defined in the enum yet. If this
                // fails: either remove the name from the list above (you finished migrating it), or
                // check that you actually completed the migration (Spec registered, Decide tested).
                bool defined = Enum.IsDefined(typeof(DiaryEventType), name);
                AssertTrue("sentinel: " + name + " is still future (not in enum)", !defined);
            }
        }

        // ── Factory helpers ──

        private const string MoodPositive = "positive";
        private const string MoodNegative = "negative";
        private const string MoodNeutral = "neutral";

        private static string MoodImpact(string token)
        {
            // Pure-side tests don't run MoodImpact.Classify (Verse-using); we only need the string.
            return token;
        }

        /// <summary>
        /// Builds a ThoughtEventData with the default test policy. Mood is classified into a token
        /// here purely for completeness — the pure Decider does not branch on the MoodImpact string,
        /// only on the numeric MoodOffset.
        /// </summary>
        private static ThoughtEventData Thought(string defName, float mood, float duration = 1f)
        {
            string impact = mood > 0.5f ? MoodPositive : (mood < -0.5f ? MoodNegative : MoodNeutral);
            return new ThoughtEventData
            {
                PawnId = "P",
                Tick = 0,
                DefName = defName,
                MoodOffset = mood,
                DurationDays = duration,
                MoodImpact = impact,
                Policy = DefaultThoughtPolicy(),
            };
        }

        private static InspirationEventData Inspiration(string defName)
        {
            return new InspirationEventData
            {
                PawnId = "P",
                Tick = 0,
                DefName = defName,
                DurationDays = 8f,
                Reason = "test reason",
            };
        }

        private static MoodEventData MoodEvent(string defName, string moodImpact = "neutral")
        {
            return new MoodEventData
            {
                PawnId = "P",
                Tick = 0,
                DefName = defName,
                Label = "test label",
                MoodImpact = moodImpact,
            };
        }

        private static MentalStateEventData MentalState(
            string defName, string otherId = null, bool otherEligible = false)
        {
            return new MentalStateEventData
            {
                PawnId = "P",
                Tick = 0,
                DefName = defName,
                OtherPawnId = otherId,
                OtherPawnEligible = otherEligible,
                OtherPawnLabel = otherId != null ? "Other" : null,
            };
        }

        private static TaleEventData Tale(
            string defName,
            bool firstEligible = true,
            bool secondEligible = false,
            bool coveredElsewhere = false,
            bool gameConditionDup = false)
        {
            return new TaleEventData
            {
                PawnId = "P1",
                Tick = 0,
                DefName = defName,
                FirstPawnId = "P1",
                SecondPawnId = secondEligible ? "P2" : null,
                FirstEligible = firstEligible,
                SecondEligible = secondEligible,
                IsCoveredElsewhere = coveredElsewhere,
                IsGameConditionDuplicate = gameConditionDup,
            };
        }

        private static HediffEventData Hediff(
            string defName = "Wound",
            bool policyRecordsSource = true,
            bool modeRecordable = true,
            bool passesPolicy = true)
        {
            return new HediffEventData
            {
                PawnId = "P",
                Tick = 0,
                DefName = defName,
                Label = "test label",
                SourceToken = "add",
                GroupKey = "hediff_test",
                ModeToken = "Immediate",
                SeverityF2 = "0.50",
                StageString = "0",
                CleanedStageLabel = null,
                CleanedBodyPartLabel = null,
                PassesPolicy = passesPolicy,
                PolicyRecordsSource = policyRecordsSource,
                ModeRecordable = modeRecordable,
            };
        }

        private static InteractionEventData Interaction(
            string defName = "Insult",
            bool initiatorEligible = true,
            bool recipientEligible = true,
            bool isSignificant = true)
        {
            return new InteractionEventData
            {
                PawnId = "P1",
                Tick = 0,
                DefName = defName,
                Label = "test label",
                InitiatorPawnId = "P1",
                RecipientPawnId = "P2",
                InitiatorEligible = initiatorEligible,
                RecipientEligible = recipientEligible,
                IsSignificant = isSignificant,
            };
        }

        private static RomanceEventData Romance(
            string defName, string firstId = "P1", string secondId = "P2",
            bool firstEligible = true, bool secondEligible = true)
        {
            return new RomanceEventData
            {
                PawnId = firstId,
                Tick = 0,
                DefName = defName,
                FirstPawnId = firstId,
                SecondPawnId = secondId,
                FirstEligible = firstEligible,
                SecondEligible = secondEligible,
            };
        }

        /// <summary>
        /// Default test policy mirrors the shipped DiarySignalPolicyDef defaults: ±5 general, ±15
        /// eating, plus tokens for testing each branch. Tokens are case-insensitive substrings.
        /// </summary>
        private static ThoughtCapturePolicy DefaultThoughtPolicy()
        {
            return new ThoughtCapturePolicy
            {
                IgnoreTokens = new List<string> { "Insult" },
                BypassThresholdTokens = new List<string> { "Death" },
                EatingTokens = new List<string> { "Ate" },
                AmbientTokens = new List<string> { "Nuzzle" },
                MinMoodOffset = 5f,
                EatingMinMoodOffset = 15f,
            };
        }

        private static CaptureContext Ctx(
            bool eligible = true,
            bool user = true,
            bool signal = true,
            bool ambient = true)
        {
            return new CaptureContext
            {
                Eligible = eligible,
                UserEnabled = user,
                SignalEnabled = signal,
                AmbientSignalEnabled = ambient,
                Now = 1000,
            };
        }

        // ── Assert helpers (mirrors DiaryPipelineTests) ──

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected: [" + expected + "]\nActual:   [" + actual + "]");
            }
        }

        private static void AssertTrue(string name, bool condition)
        {
            assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(name + " failed.");
            }
        }
    }
}

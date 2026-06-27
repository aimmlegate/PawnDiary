// Pure unit tests for the Event Catalog decision layer. These exercise each migrated EventData.Decide
// reducer and the DiaryEventCatalog dispatch without RimWorld assemblies. Run via: build
// DiaryCapturePolicyTests.csproj, then execute the resulting exe (exit code 0 = pass).
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
            TestRaidDecide();
            TestRaidBuildGameContextFormat();
            TestQuestDecide();
            TestQuestBuildDisplayLabel();
            TestQuestBuildGameContextFormat();
            TestRitualDecide();
            TestRitualQualityLabel();
            TestRitualBuildGameContextFormat();
            TestPsychicRitualBuildGameContextFormat();
            TestAbilityDecide();
            TestAbilityCooldownWeightedChance();
            TestAbilityBuildGameContextFormat();
            TestArrivalDecide();
            TestArrivalBuildGameContextFormat();
            TestDeathDecide();
            TestDeathBuildFallbackGameContextFormat();
            TestWorkDecide();
            TestWorkEventDefName();
            TestWorkBuildGameContextFormat();
            TestThoughtProgressionDecide();
            TestThoughtProgressionBuildGameContextFormat();
            TestDayReflectionDecide();
            TestDayReflectionImportantSignalKindPolicy();
            TestDayReflectionBuildGameContextFormat();
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

        // ── Tale ──

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

            // Final shape: single eligible pawn → solo.
            AssertEqual("tale first-only eligible generates solo", CaptureDecision.GenerateSolo,
                TaleEventData.Decide(Tale("KilledMan", firstEligible: true), Ctx()));
            AssertEqual("tale second-only eligible generates solo", CaptureDecision.GenerateSolo,
                TaleEventData.Decide(Tale("Wounded", firstEligible: false, secondEligible: true), Ctx()));

            // Final shape: non-death pair, batch, and death-description routes.
            AssertEqual("tale both eligible generates pair", CaptureDecision.GeneratePair,
                TaleEventData.Decide(Tale("DidResearch", firstEligible: true, secondEligible: true), Ctx()));
            AssertEqual("tale same pawn stays solo", CaptureDecision.GenerateSolo,
                TaleEventData.Decide(Tale("DidResearch", firstEligible: true, secondEligible: true, secondId: "P1"), Ctx()));
            AssertEqual("tale batched route", CaptureDecision.RouteBatch,
                TaleEventData.Decide(Tale("Wounded", firstEligible: true, secondEligible: true, batched: true), Ctx()));
            AssertEqual("tale solo death description", CaptureDecision.GenerateSoloDeathDescription,
                TaleEventData.Decide(Tale("Died", firstEligible: true, deathDescription: true), Ctx()));
            AssertEqual("tale pair death description", CaptureDecision.GeneratePairDeathDescription,
                TaleEventData.Decide(Tale("KilledMan", firstEligible: true, secondEligible: true, deathDescription: true), Ctx()));
            AssertEqual("tale death beats batch", CaptureDecision.GeneratePairDeathDescription,
                TaleEventData.Decide(Tale("KilledMan", firstEligible: true, secondEligible: true, batched: true, deathDescription: true), Ctx()));
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

        // ── Hediff ──

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
            AssertEqual("hediff signal disabled drops", CaptureDecision.Drop,
                HediffEventData.Decide(Hediff(), Ctx(signal: false)));

            // Policy gates: PolicyRecordsSource / ModeRecordable / PassesPolicy (any false → Drop).
            AssertEqual("hediff policy not recording source drops", CaptureDecision.Drop,
                HediffEventData.Decide(Hediff(policyRecordsSource: false), Ctx()));
            AssertEqual("hediff mode not recordable drops", CaptureDecision.Drop,
                HediffEventData.Decide(Hediff(modeRecordable: false), Ctx()));
            AssertEqual("hediff fails policy drops", CaptureDecision.Drop,
                HediffEventData.Decide(Hediff(passesPolicy: false), Ctx()));

            // Final route: Immediate emits a solo event; DayReflection feeds the day-summary collector.
            AssertEqual("hediff immediate generates solo", CaptureDecision.GenerateSolo,
                HediffEventData.Decide(Hediff(), Ctx()));
            AssertEqual("hediff day reflection routes", CaptureDecision.RouteDayReflection,
                HediffEventData.Decide(Hediff(modeToken: "DayReflection"), Ctx()));
            AssertEqual("hediff day reflection route is case-insensitive", CaptureDecision.RouteDayReflection,
                HediffEventData.Decide(Hediff(modeToken: "dayreflection"), Ctx()));
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

        // ── Interaction ──

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
            AssertEqual("interaction signal disabled drops", CaptureDecision.Drop,
                InteractionEventData.Decide(Interaction(), Ctx(signal: false)));

            // No eligible pawn → Drop.
            AssertEqual("interaction no eligible pawns drops", CaptureDecision.Drop,
                InteractionEventData.Decide(
                    Interaction(initiatorEligible: false, recipientEligible: false),
                    Ctx()));

            // Final shape: one eligible pawn stays solo.
            AssertEqual("interaction initiator only eligible generates solo", CaptureDecision.GenerateSolo,
                InteractionEventData.Decide(
                    Interaction(initiatorEligible: true, recipientEligible: false), Ctx()));
            AssertEqual("interaction recipient only eligible generates solo", CaptureDecision.GenerateSolo,
                InteractionEventData.Decide(
                    Interaction(initiatorEligible: false, recipientEligible: true), Ctx()));

            // Final shape: both eligible pawns either pair immediately, route to batch, or ambient.
            AssertEqual("interaction both eligible generates pair", CaptureDecision.GeneratePair,
                InteractionEventData.Decide(Interaction(), Ctx()));
            AssertEqual("interaction route batch", CaptureDecision.RouteBatch,
                InteractionEventData.Decide(Interaction(routeToBatch: true), Ctx()));
            AssertEqual("interaction route ambient", CaptureDecision.RouteAmbient,
                InteractionEventData.Decide(Interaction(routeToAmbient: true), Ctx()));
            AssertEqual("interaction ambient wins over batch flag", CaptureDecision.RouteAmbient,
                InteractionEventData.Decide(Interaction(routeToBatch: true, routeToAmbient: true), Ctx()));
            AssertEqual("interaction route flag ignored for one eligible pawn", CaptureDecision.GenerateSolo,
                InteractionEventData.Decide(
                    Interaction(initiatorEligible: true, recipientEligible: false, routeToBatch: true), Ctx()));
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

        // ── Raid (colony-wide fan-out, delayed ordinary raids, immediate drop pods/infestations) ──

        private static void TestRaidDecide()
        {
            // Null guards.
            AssertEqual("raid null data drops", CaptureDecision.Drop,
                RaidEventData.Decide(null, Ctx()));
            AssertEqual("raid null ctx drops", CaptureDecision.Drop,
                RaidEventData.Decide(Raid(), null));

            // Eligibility / user gate (mirrors MoodEvent: trivial eligible + user shape).
            AssertEqual("raid ineligible drops", CaptureDecision.Drop,
                RaidEventData.Decide(Raid(), Ctx(eligible: false)));
            AssertEqual("raid user disabled drops", CaptureDecision.Drop,
                RaidEventData.Decide(Raid(), Ctx(user: false)));
            AssertEqual("raid eligible records solo", CaptureDecision.GenerateSolo,
                RaidEventData.Decide(Raid(), Ctx()));
            AssertEqual("ordinary raid delay", true,
                RaidEventData.ShouldDelayGeneration("RaidEnemy", "EdgeWalkIn", "ImmediateAttack", 2500));
            AssertEqual("zero delay disables raid delay", false,
                RaidEventData.ShouldDelayGeneration("RaidEnemy", "EdgeWalkIn", "ImmediateAttack", 0));
            AssertEqual("drop pod arrival bypasses raid delay", false,
                RaidEventData.ShouldDelayGeneration("RaidEnemy", "CenterDrop", "ImmediateAttack", 2500));
            AssertEqual("drop pod strategy bypasses raid delay", false,
                RaidEventData.ShouldDelayGeneration("RaidEnemy", "EdgeWalkIn", "ImmediateAttackSmartDrop", 2500));
            AssertEqual("infestation bypasses raid delay", false,
                RaidEventData.ShouldDelayGeneration("Infestation", null, null, 2500));
        }

        private static void TestRaidBuildGameContextFormat()
        {
            // The leading "raid=" marker is load-bearing for UI domain classification. Field order
            // and the int points format are locked here so a future migration cannot drift them.
            AssertEqual("raid enemy context",
                "raid=RaidEnemy; label=enemy raid; faction=Pirate; points=350",
                RaidEventData.BuildGameContext("RaidEnemy", "enemy raid", "Pirate", "350"));
            AssertEqual("raid unknown faction sentinel",
                "raid=RaidFriendly; label=friendly raid; faction=unknown; points=0",
                RaidEventData.BuildGameContext("RaidFriendly", "friendly raid", "unknown", "0"));
            AssertEqual("raid arrival and strategy context",
                "raid=RaidEnemy; label=enemy raid; faction=Pirate; points=350; arrival_mode=CenterDrop; strategy=ImmediateAttack",
                RaidEventData.BuildGameContext("RaidEnemy", "enemy raid", "Pirate", "350",
                    "CenterDrop", "ImmediateAttack"));
        }

        // ── Quest (lifecycle: accepted / completed / failed) ──

        private static void TestQuestDecide()
        {
            // Null guards.
            AssertEqual("quest null data drops", CaptureDecision.Drop,
                QuestEventData.Decide(null, Ctx()));
            AssertEqual("quest null ctx drops", CaptureDecision.Drop,
                QuestEventData.Decide(Quest("accepted"), null));

            // The "do not record offered-but-not-accepted" requirement is enforced here: an empty
            // signal drops (defensive — the hook layer never constructs such a payload, but the pure
            // layer guards independently).
            AssertEqual("quest empty signal drops (offered not accepted)", CaptureDecision.Drop,
                QuestEventData.Decide(Quest(""), Ctx()));
            AssertEqual("quest null signal drops", CaptureDecision.Drop,
                QuestEventData.Decide(Quest(null), Ctx()));

            // Eligibility / user gate.
            AssertEqual("quest ineligible drops", CaptureDecision.Drop,
                QuestEventData.Decide(Quest("accepted"), Ctx(eligible: false)));
            AssertEqual("quest user disabled drops", CaptureDecision.Drop,
                QuestEventData.Decide(Quest("completed"), Ctx(user: false)));

            // All three lifecycle signals record through the same Decide.
            AssertEqual("quest accepted records solo", CaptureDecision.GenerateSolo,
                QuestEventData.Decide(Quest("accepted"), Ctx()));
            AssertEqual("quest completed records solo", CaptureDecision.GenerateSolo,
                QuestEventData.Decide(Quest("completed"), Ctx()));
            AssertEqual("quest failed records solo", CaptureDecision.GenerateSolo,
                QuestEventData.Decide(Quest("failed"), Ctx()));
        }

        private static void TestQuestBuildDisplayLabel()
        {
            AssertEqual("quest display keeps natural generated name",
                "A Stolen Cache",
                QuestEventData.BuildDisplayLabel("A Stolen Cache", "OpportunityQuest_Friendlies", "OpportunityQuest_Friendlies"));
            AssertEqual("quest display rejects placeholder generated name",
                "Opportunity Friendlies",
                QuestEventData.BuildDisplayLabel("QuestName", "OpportunityQuest_Friendlies", "OpportunityQuest_Friendlies"));
            AssertEqual("quest display humanizes defName fallback and cuts Quest",
                "Ancient Complex Threat",
                QuestEventData.BuildDisplayLabel("", "", "AncientComplexQuest_Threat"));
            AssertEqual("quest display humanizes pascal fallback",
                "Refugee Chased",
                QuestEventData.BuildDisplayLabel(null, null, "RefugeeChased"));
        }

        private static void TestQuestBuildGameContextFormat()
        {
            // The leading "quest=" marker is load-bearing for UI domain classification; the signal
            // field routes prompt group selection. The description is intentionally NOT here (it
            // is prose and lives in the localized event text). Field order locked by this test.
            AssertEqual("quest accepted context",
                "quest=OpportunityQuest_Friendlies; signal=accepted; label=A Stolen Cache; faction=Outlander; rewards=Silver x100, Medicine x5; quest_label=A Stolen Cache; quest_signal=accepted; quest_faction=Outlander; quest_rewards=Silver x100, Medicine x5",
                QuestEventData.BuildGameContext("OpportunityQuest_Friendlies", "accepted", "A Stolen Cache", "Outlander", "Silver x100, Medicine x5"));
            AssertEqual("quest completed sentinels",
                "quest=UntitledQuest; signal=completed; label=untitled; faction=unknown; rewards=none; quest_label=untitled; quest_signal=completed; quest_faction=unknown; quest_rewards=none",
                QuestEventData.BuildGameContext("UntitledQuest", "completed", "untitled", "unknown", "none"));
            AssertEqual("quest failed context",
                "quest=ThreatQuest; signal=failed; label=the thrumbo pulse; faction=Pirate; rewards=none; quest_label=the thrumbo pulse; quest_signal=failed; quest_faction=Pirate; quest_rewards=none",
                QuestEventData.BuildGameContext("ThreatQuest", "failed", "the thrumbo pulse", "Pirate", "none"));
        }

        // ── Ritual (finished Ideology rituals) ──

        private static void TestRitualDecide()
        {
            AssertEqual("ritual null data drops", CaptureDecision.Drop,
                RitualEventData.Decide(null, Ctx()));
            AssertEqual("ritual null ctx drops", CaptureDecision.Drop,
                RitualEventData.Decide(Ritual(), null));
            AssertEqual("ritual empty defName drops", CaptureDecision.Drop,
                RitualEventData.Decide(Ritual(defName: ""), Ctx()));
            AssertEqual("ritual cancelled drops", CaptureDecision.Drop,
                RitualEventData.Decide(Ritual(cancelled: true), Ctx()));
            AssertEqual("ritual ineligible drops", CaptureDecision.Drop,
                RitualEventData.Decide(Ritual(), Ctx(eligible: false)));
            AssertEqual("ritual user disabled drops", CaptureDecision.Drop,
                RitualEventData.Decide(Ritual(), Ctx(user: false)));
            AssertEqual("ritual signal disabled drops", CaptureDecision.Drop,
                RitualEventData.Decide(Ritual(), Ctx(signal: false)));
            AssertEqual("ritual finished records solo", CaptureDecision.GenerateSolo,
                RitualEventData.Decide(Ritual(), Ctx()));
        }

        private static void TestRitualBuildGameContextFormat()
        {
            AssertEqual("ritual context full",
                "ritual=Ritual_Speech; ritual_title=Leader's address; ritual_behavior=RitualBehaviorWorker_LeaderSpeech; ritual_perspective=author; ritual_role=author (speaker); royal_title=Count; ideological_role=Moral guide; outcome=finished; quality=strong",
                RitualEventData.BuildGameContext(
                    "Ritual_Speech",
                    "Leader's address",
                    "RitualBehaviorWorker_LeaderSpeech",
                    "author",
                    "author (speaker)",
                    "Count",
                    "Moral guide",
                    "finished",
                    "strong"));
            AssertEqual("ritual context fallbacks",
                "ritual=Ritual_Dance; ritual_title=ritual; ritual_behavior=unknown; ritual_perspective=participant; ritual_role=participant; royal_title=none; ideological_role=none; outcome=finished; quality=unknown",
                RitualEventData.BuildGameContext(
                    "Ritual_Dance",
                    "",
                    "",
                    "",
                    "",
                    "",
                    null,
                    "",
                    ""));
        }

        private static void TestPsychicRitualBuildGameContextFormat()
        {
            string context = RitualEventData.BuildPsychicGameContext(
                "VoidProvocation",
                RitualEventData.PerspectiveInvoker,
                "finished",
                "decent");
            AssertEqual("psychic ritual context full",
                "psychic_ritual=VoidProvocation; psychic_ritual_perspective=invoker; outcome=finished; quality=decent",
                context);
            AssertTrue("psychic ritual omits ritual title",
                context.IndexOf("ritual_title=", StringComparison.OrdinalIgnoreCase) < 0);
            AssertTrue("psychic ritual omits ritual role",
                context.IndexOf("ritual_role=", StringComparison.OrdinalIgnoreCase) < 0);

            AssertEqual("psychic ritual context fallbacks",
                "psychic_ritual=Chronophagy; psychic_ritual_perspective=participant; outcome=finished; quality=unknown",
                RitualEventData.BuildPsychicGameContext("Chronophagy", "", "", ""));
        }

        private static void TestRitualQualityLabel()
        {
            List<RitualQualityBand> bands = RitualEventData.DefaultQualityBands();
            AssertEqual("ritual quality NaN", "unknown", RitualEventData.QualityLabel(float.NaN, bands));
            AssertEqual("ritual quality terrible", "terrible", RitualEventData.QualityLabel(0.1f, bands));
            AssertEqual("ritual quality weak", "weak", RitualEventData.QualityLabel(0.35f, bands));
            AssertEqual("ritual quality decent", "decent", RitualEventData.QualityLabel(0.67f, bands));
            AssertEqual("ritual quality strong", "strong", RitualEventData.QualityLabel(0.84f, bands));
            AssertEqual("ritual quality excellent", "excellent", RitualEventData.QualityLabel(1f, bands));

            List<RitualQualityBand> customBands = new List<RitualQualityBand>
            {
                new RitualQualityBand { maxExclusive = 0.5f, label = "low" },
                new RitualQualityBand { maxExclusive = 9999f, label = "high" },
            };
            AssertEqual("ritual quality uses supplied bands", "high",
                RitualEventData.QualityLabel(0.75f, customBands));
        }

        // ── Ability (successful Ability.Activate calls) ──

        private static void TestAbilityDecide()
        {
            AssertEqual("ability null data drops", CaptureDecision.Drop,
                AbilityEventData.Decide(null, Ctx()));
            AssertEqual("ability null ctx drops", CaptureDecision.Drop,
                AbilityEventData.Decide(Ability(), null));
            AssertEqual("ability empty defName drops", CaptureDecision.Drop,
                AbilityEventData.Decide(Ability(defName: ""), Ctx()));
            AssertEqual("ability ineligible drops", CaptureDecision.Drop,
                AbilityEventData.Decide(Ability(), Ctx(eligible: false)));
            AssertEqual("ability user disabled drops", CaptureDecision.Drop,
                AbilityEventData.Decide(Ability(), Ctx(user: false)));
            AssertEqual("ability signal disabled drops", CaptureDecision.Drop,
                AbilityEventData.Decide(Ability(), Ctx(signal: false)));
            AssertEqual("ability roll above chance drops", CaptureDecision.Drop,
                AbilityEventData.Decide(Ability(chance: 0.25f, roll: 0.251f), Ctx()));
            AssertEqual("ability roll at chance records solo", CaptureDecision.GenerateSolo,
                AbilityEventData.Decide(Ability(chance: 0.25f, roll: 0.25f), Ctx()));
            AssertEqual("ability chance clamps high", CaptureDecision.GenerateSolo,
                AbilityEventData.Decide(Ability(chance: 2f, roll: 1f), Ctx()));
        }

        private static void TestAbilityCooldownWeightedChance()
        {
            AssertNearlyEqual("zero cooldown uses min chance", 0.03f,
                AbilityEventData.CooldownWeightedChance(0, 0.03f, 0.75f, 60000));
            AssertNearlyEqual("reference cooldown is halfway through curve", 0.39f,
                AbilityEventData.CooldownWeightedChance(60000, 0.03f, 0.75f, 60000));
            AssertTrue("long cooldown has more weight than reference",
                AbilityEventData.CooldownWeightedChance(240000, 0.03f, 0.75f, 60000)
                > AbilityEventData.CooldownWeightedChance(60000, 0.03f, 0.75f, 60000));
            AssertNearlyEqual("swapped min max are corrected", 0.39f,
                AbilityEventData.CooldownWeightedChance(60000, 0.75f, 0.03f, 60000));
            AssertNearlyEqual("negative cooldown is treated as zero", 0.03f,
                AbilityEventData.CooldownWeightedChance(-10, 0.03f, 0.75f, 60000));
        }

        private static void TestAbilityBuildGameContextFormat()
        {
            AssertEqual("ability context full",
                "ability=Stun; ability_label=stun; ability_category=Psycast; ability_cooldown_ticks=600; ability_record_chance=0.037; ability_target=Bob",
                AbilityEventData.BuildGameContext("Stun", "stun", "Psycast", "Bob", 600, 0.0371f));
            AssertEqual("ability context fallbacks",
                "ability=JumpPack; ability_label=JumpPack; ability_category=unknown; ability_cooldown_ticks=0; ability_record_chance=1",
                AbilityEventData.BuildGameContext("JumpPack", "", "", "", -20, 5f));
        }

        // ── Arrival ──

        private static void TestArrivalDecide()
        {
            AssertEqual("arrival null data drops", CaptureDecision.Drop,
                ArrivalEventData.Decide(null, Ctx()));
            AssertEqual("arrival null ctx drops", CaptureDecision.Drop,
                ArrivalEventData.Decide(Arrival(), null));
            AssertEqual("arrival empty defName drops", CaptureDecision.Drop,
                ArrivalEventData.Decide(Arrival(defName: ""), Ctx()));
            AssertEqual("arrival ineligible drops", CaptureDecision.Drop,
                ArrivalEventData.Decide(Arrival(), Ctx(eligible: false)));
            AssertEqual("arrival user disabled drops", CaptureDecision.Drop,
                ArrivalEventData.Decide(Arrival(), Ctx(user: false)));
            AssertEqual("arrival signal disabled drops", CaptureDecision.Drop,
                ArrivalEventData.Decide(Arrival(), Ctx(signal: false)));
            AssertEqual("arrival existing page drops", CaptureDecision.Drop,
                ArrivalEventData.Decide(Arrival(existing: true), Ctx()));
            AssertEqual("arrival records through neutral prompt route",
                CaptureDecision.GenerateSoloArrivalDescription,
                ArrivalEventData.Decide(Arrival(), Ctx()));
        }

        private static void TestArrivalBuildGameContextFormat()
        {
            AssertTrue("arrival game-start marker is case-insensitive",
                ArrivalEventData.IsStartingArrival("arrival_source=GAME_START; scenario=Crashlanded"));
            AssertTrue("arrival non-start marker is false",
                !ArrivalEventData.IsStartingArrival("arrival_source=joined"));

            AssertEqual("arrival context with supplied source",
                "arrival_description=true; arrival_pawn=Alice; arrival_pawn_id=P1; arrival_source=joined; join_reason=rescued",
                ArrivalEventData.BuildGameContext("Alice", "P1", "arrival_source=joined; join_reason=rescued"));
            AssertEqual("arrival context unknown source fallback",
                "arrival_description=true; arrival_pawn=Alice; arrival_pawn_id=P1; arrival_source=unknown",
                ArrivalEventData.BuildGameContext("Alice", "P1", ""));
        }

        // ── Death fallback ──

        private static void TestDeathDecide()
        {
            AssertEqual("death null data drops", CaptureDecision.Drop,
                DeathEventData.Decide(null, Ctx()));
            AssertEqual("death null ctx drops", CaptureDecision.Drop,
                DeathEventData.Decide(Death(), null));
            AssertEqual("death empty defName drops", CaptureDecision.Drop,
                DeathEventData.Decide(Death(defName: ""), Ctx()));
            AssertEqual("death ineligible drops", CaptureDecision.Drop,
                DeathEventData.Decide(Death(), Ctx(eligible: false)));
            AssertEqual("death user disabled drops", CaptureDecision.Drop,
                DeathEventData.Decide(Death(), Ctx(user: false)));
            AssertEqual("death signal disabled drops", CaptureDecision.Drop,
                DeathEventData.Decide(Death(), Ctx(signal: false)));
            AssertEqual("death existing description drops", CaptureDecision.Drop,
                DeathEventData.Decide(Death(existing: true), Ctx()));
            AssertEqual("death records through neutral prompt route",
                CaptureDecision.GenerateSoloDeathDescription,
                DeathEventData.Decide(Death(), Ctx()));
        }

        private static void TestDeathBuildFallbackGameContextFormat()
        {
            AssertEqual("death fallback context with facts",
                "tale=PawnDiary_DeathFallback; label=death; taleClass=PawnKillFallback; death_description=true; death_victim=Alice; death_victim_id=P1; death_victim_role=initiator; killer=Bear; cause=mauling",
                DeathEventData.BuildFallbackGameContext(
                    "PawnDiary_DeathFallback", "death", "Alice", "P1", "initiator", "killer=Bear; cause=mauling"));
            AssertEqual("death fallback context without facts",
                "tale=PawnDiary_DeathFallback; label=death; taleClass=PawnKillFallback; death_description=true; death_victim=Alice; death_victim_id=P1; death_victim_role=initiator",
                DeathEventData.BuildFallbackGameContext(
                    "PawnDiary_DeathFallback", "death", "Alice", "P1", "initiator", ""));
        }

        // ── Work ──

        private static void TestWorkDecide()
        {
            AssertEqual("work null data drops", CaptureDecision.Drop,
                WorkEventData.Decide(null, Ctx()));
            AssertEqual("work null ctx drops", CaptureDecision.Drop,
                WorkEventData.Decide(Work(), null));
            AssertEqual("work empty defName drops", CaptureDecision.Drop,
                WorkEventData.Decide(Work(defName: ""), Ctx()));
            AssertEqual("work ineligible drops", CaptureDecision.Drop,
                WorkEventData.Decide(Work(), Ctx(eligible: false)));
            AssertEqual("work user disabled drops", CaptureDecision.Drop,
                WorkEventData.Decide(Work(), Ctx(user: false)));
            AssertEqual("work signal disabled drops", CaptureDecision.Drop,
                WorkEventData.Decide(Work(), Ctx(signal: false)));
            AssertEqual("work without current work drops", CaptureDecision.Drop,
                WorkEventData.Decide(Work(hasCurrentWork: false), Ctx()));
            AssertEqual("work ignored type drops", CaptureDecision.Drop,
                WorkEventData.Decide(Work(ignored: true), Ctx()));
            AssertEqual("work same-type cooldown drops", CaptureDecision.Drop,
                WorkEventData.Decide(Work(cooldownClear: false), Ctx()));
            AssertEqual("work failed chance roll drops", CaptureDecision.Drop,
                WorkEventData.Decide(Work(passedChance: false), Ctx()));
            AssertEqual("work valid signal records", CaptureDecision.GenerateSolo,
                WorkEventData.Decide(Work(), Ctx()));
        }

        private static void TestWorkEventDefName()
        {
            AssertEqual("work dark study def wins", WorkEventData.DarkStudyDefName,
                WorkEventData.EventDefName(isDarkStudy: true, isPositive: true, isNegative: true));
            AssertEqual("work positive def", WorkEventData.PassionDefName,
                WorkEventData.EventDefName(isDarkStudy: false, isPositive: true, isNegative: false));
            AssertEqual("work negative def", WorkEventData.StrainDefName,
                WorkEventData.EventDefName(isDarkStudy: false, isPositive: false, isNegative: true));
            AssertEqual("work routine def", WorkEventData.RoutineDefName,
                WorkEventData.EventDefName(isDarkStudy: false, isPositive: false, isNegative: false));
        }

        private static void TestWorkBuildGameContextFormat()
        {
            AssertEqual("work context with flags",
                "work=Cooking; work_giver=DoBill; mood_impact=positive; passion=true; low_skill=true; dumb_or_cleaning=true; dark_study=true",
                WorkEventData.BuildGameContext(
                    "Cooking", "DoBill", MoodPositive, true, true, true, true));
            AssertEqual("work context null strings and false flags",
                "work=; work_giver=; mood_impact=; passion=false; low_skill=false; dumb_or_cleaning=false; dark_study=false",
                WorkEventData.BuildGameContext(
                    null, null, null, false, false, false, false));
        }

        // ── Thought progression ──

        private static void TestThoughtProgressionDecide()
        {
            AssertEqual("thought progression null data drops", CaptureDecision.Drop,
                ThoughtProgressionEventData.Decide(null, Ctx()));
            AssertEqual("thought progression null ctx drops", CaptureDecision.Drop,
                ThoughtProgressionEventData.Decide(ThoughtProgression(), null));
            AssertEqual("thought progression empty defName drops", CaptureDecision.Drop,
                ThoughtProgressionEventData.Decide(ThoughtProgression(defName: ""), Ctx()));
            AssertEqual("thought progression ineligible drops", CaptureDecision.Drop,
                ThoughtProgressionEventData.Decide(ThoughtProgression(), Ctx(eligible: false)));
            AssertEqual("thought progression user disabled drops", CaptureDecision.Drop,
                ThoughtProgressionEventData.Decide(ThoughtProgression(), Ctx(user: false)));
            AssertEqual("thought progression signal disabled drops", CaptureDecision.Drop,
                ThoughtProgressionEventData.Decide(ThoughtProgression(), Ctx(signal: false)));
            AssertEqual("thought progression not worsened drops", CaptureDecision.Drop,
                ThoughtProgressionEventData.Decide(ThoughtProgression(worsened: false), Ctx()));
            AssertEqual("thought progression already recorded drops", CaptureDecision.Drop,
                ThoughtProgressionEventData.Decide(ThoughtProgression(stageRecorded: true), Ctx()));
            AssertEqual("thought progression worsened records", CaptureDecision.GenerateSolo,
                ThoughtProgressionEventData.Decide(ThoughtProgression(), Ctx()));
        }

        private static void TestThoughtProgressionBuildGameContextFormat()
        {
            AssertEqual("thought progression context",
                "thought=NeedOutdoors; thought_progression=need_outdoors; label=stuck indoors; stage_index=2; severity=0.75; mood_impact=negative; mood_offset=-8.0",
                ThoughtProgressionEventData.BuildGameContext(
                    "NeedOutdoors", "need_outdoors", "stuck indoors", "2", "0.75", MoodNegative, "-8.0"));
        }

        // ── Day reflection ──

        private static void TestDayReflectionDecide()
        {
            AssertEqual("day reflection null data drops", CaptureDecision.Drop,
                DayReflectionEventData.Decide(null, Ctx()));
            AssertEqual("day reflection null ctx drops", CaptureDecision.Drop,
                DayReflectionEventData.Decide(DayReflection(), null));
            AssertEqual("day reflection empty defName drops", CaptureDecision.Drop,
                DayReflectionEventData.Decide(DayReflection(defName: ""), Ctx()));
            AssertEqual("day reflection ineligible drops", CaptureDecision.Drop,
                DayReflectionEventData.Decide(DayReflection(), Ctx(eligible: false)));
            AssertEqual("day reflection user disabled drops", CaptureDecision.Drop,
                DayReflectionEventData.Decide(DayReflection(), Ctx(user: false)));
            AssertEqual("day reflection signal disabled drops", CaptureDecision.Drop,
                DayReflectionEventData.Decide(DayReflection(), Ctx(signal: false)));
            AssertEqual("day reflection already written drops", CaptureDecision.Drop,
                DayReflectionEventData.Decide(DayReflection(alreadyWritten: true), Ctx()));
            AssertEqual("day reflection no candidates drops", CaptureDecision.Drop,
                DayReflectionEventData.Decide(DayReflection(candidates: 0), Ctx()));
            AssertEqual("day reflection no highlights drops", CaptureDecision.Drop,
                DayReflectionEventData.Decide(DayReflection(highlights: 0), Ctx()));
            AssertEqual("day reflection filler-only candidates drop", CaptureDecision.Drop,
                DayReflectionEventData.Decide(DayReflection(candidates: 2, highlights: 1, importantCandidates: 0), Ctx()));
            AssertEqual("day reflection valid signal records", CaptureDecision.GenerateSolo,
                DayReflectionEventData.Decide(DayReflection(), Ctx()));
        }

        private static void TestDayReflectionImportantSignalKindPolicy()
        {
            List<string> policy = new List<string>
            {
                DayReflectionEventData.SignalKindEvent,
                DayReflectionEventData.SignalKindHediff,
            };

            AssertTrue("day reflection important kind matches exact token",
                DayReflectionEventData.IsImportantSignalKind(DayReflectionEventData.SignalKindEvent, policy));
            AssertTrue("day reflection important kind is case-insensitive",
                DayReflectionEventData.IsImportantSignalKind("HEDIFF", policy));
            AssertTrue("day reflection non-listed kind is not important",
                !DayReflectionEventData.IsImportantSignalKind(DayReflectionEventData.SignalKindFiller, policy));
            AssertTrue("day reflection empty policy disables all kinds",
                !DayReflectionEventData.IsImportantSignalKind(DayReflectionEventData.SignalKindEvent, new List<string>()));
            AssertTrue("day reflection null policy disables all kinds",
                !DayReflectionEventData.IsImportantSignalKind(DayReflectionEventData.SignalKindEvent, null));
            AssertTrue("day reflection blank kind never matches",
                !DayReflectionEventData.IsImportantSignalKind(" ", policy));
        }

        private static void TestDayReflectionBuildGameContextFormat()
        {
            AssertEqual("day reflection context",
                "day_reflection=true; day=42; highlights=3; candidates=8; filler_moments=2; signals=thought,work,hediff",
                DayReflectionEventData.BuildGameContext(42, 3, 8, 2, "thought,work,hediff"));
            AssertEqual("day reflection context null tags",
                "day_reflection=true; day=42; highlights=1; candidates=1; filler_moments=0; signals=",
                DayReflectionEventData.BuildGameContext(42, 1, 1, 0, null));
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

            DiaryEventSpec moodSpec = DiaryEventCatalog.Get(DiaryEventType.MoodEvent);
            AssertTrue("catalog has MoodEvent spec", moodSpec is MoodEventSpec);
            AssertEqual("catalog dispatches MoodEvent decision",
                CaptureDecision.GenerateSolo,
                moodSpec.Decide(MoodEvent("Aurora"), Ctx()));

            DiaryEventSpec mentalStateSpec = DiaryEventCatalog.Get(DiaryEventType.MentalState);
            AssertTrue("catalog has MentalState spec", mentalStateSpec is MentalStateEventSpec);
            AssertEqual("catalog dispatches MentalState decision",
                CaptureDecision.GenerateSolo,
                mentalStateSpec.Decide(MentalState("Berserk"), Ctx()));

            DiaryEventSpec taleSpec = DiaryEventCatalog.Get(DiaryEventType.Tale);
            AssertTrue("catalog has Tale spec", taleSpec is TaleEventSpec);
            AssertEqual("catalog dispatches Tale decision",
                CaptureDecision.GenerateSolo,
                taleSpec.Decide(Tale("KilledMan", firstEligible: true), Ctx()));

            DiaryEventSpec hediffSpec = DiaryEventCatalog.Get(DiaryEventType.Hediff);
            AssertTrue("catalog has Hediff spec", hediffSpec is HediffEventSpec);
            AssertEqual("catalog dispatches Hediff decision",
                CaptureDecision.GenerateSolo,
                hediffSpec.Decide(Hediff(), Ctx()));

            DiaryEventSpec interactionSpec = DiaryEventCatalog.Get(DiaryEventType.Interaction);
            AssertTrue("catalog has Interaction spec", interactionSpec is InteractionEventSpec);
            AssertEqual("catalog dispatches Interaction decision",
                CaptureDecision.GeneratePair,
                interactionSpec.Decide(Interaction(), Ctx()));

            DiaryEventSpec romanceSpec = DiaryEventCatalog.Get(DiaryEventType.Romance);
            AssertTrue("catalog has Romance spec", romanceSpec is RomanceEventSpec);
            AssertEqual("catalog dispatches Romance decision",
                CaptureDecision.GeneratePair,
                romanceSpec.Decide(Romance("Lover"), Ctx()));

            DiaryEventSpec raidSpec = DiaryEventCatalog.Get(DiaryEventType.Raid);
            AssertTrue("catalog has Raid spec", raidSpec is RaidEventSpec);
            AssertEqual("catalog dispatches Raid decision",
                CaptureDecision.GenerateSolo,
                raidSpec.Decide(Raid(), Ctx()));

            DiaryEventSpec questSpec = DiaryEventCatalog.Get(DiaryEventType.Quest);
            AssertTrue("catalog has Quest spec", questSpec is QuestEventSpec);
            AssertEqual("catalog dispatches Quest decision",
                CaptureDecision.GenerateSolo,
                questSpec.Decide(Quest("accepted"), Ctx()));

            DiaryEventSpec ritualSpec = DiaryEventCatalog.Get(DiaryEventType.Ritual);
            AssertTrue("catalog has Ritual spec", ritualSpec is RitualEventSpec);
            AssertEqual("catalog dispatches Ritual decision",
                CaptureDecision.GenerateSolo,
                ritualSpec.Decide(Ritual(), Ctx()));

            DiaryEventSpec abilitySpec = DiaryEventCatalog.Get(DiaryEventType.Ability);
            AssertTrue("catalog has Ability spec", abilitySpec is AbilityEventSpec);
            AssertEqual("catalog dispatches Ability decision",
                CaptureDecision.GenerateSolo,
                abilitySpec.Decide(Ability(), Ctx()));

            DiaryEventSpec arrivalSpec = DiaryEventCatalog.Get(DiaryEventType.Arrival);
            AssertTrue("catalog has Arrival spec", arrivalSpec is ArrivalEventSpec);
            AssertEqual("catalog dispatches Arrival decision",
                CaptureDecision.GenerateSoloArrivalDescription,
                arrivalSpec.Decide(Arrival(), Ctx()));

            DiaryEventSpec deathSpec = DiaryEventCatalog.Get(DiaryEventType.Death);
            AssertTrue("catalog has Death spec", deathSpec is DeathEventSpec);
            AssertEqual("catalog dispatches Death decision",
                CaptureDecision.GenerateSoloDeathDescription,
                deathSpec.Decide(Death(), Ctx()));

            DiaryEventSpec workSpec = DiaryEventCatalog.Get(DiaryEventType.Work);
            AssertTrue("catalog has Work spec", workSpec is WorkEventSpec);
            AssertEqual("catalog dispatches Work decision",
                CaptureDecision.GenerateSolo,
                workSpec.Decide(Work(), Ctx()));

            DiaryEventSpec thoughtProgressionSpec = DiaryEventCatalog.Get(DiaryEventType.ThoughtProgression);
            AssertTrue("catalog has ThoughtProgression spec", thoughtProgressionSpec is ThoughtProgressionEventSpec);
            AssertEqual("catalog dispatches ThoughtProgression decision",
                CaptureDecision.GenerateSolo,
                thoughtProgressionSpec.Decide(ThoughtProgression(), Ctx()));

            DiaryEventSpec dayReflectionSpec = DiaryEventCatalog.Get(DiaryEventType.DayReflection);
            AssertTrue("catalog has DayReflection spec", dayReflectionSpec is DayReflectionEventSpec);
            AssertEqual("catalog dispatches DayReflection decision",
                CaptureDecision.GenerateSolo,
                dayReflectionSpec.Decide(DayReflection(), Ctx()));

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
            "MajorThreat", "RandomEvent", "WorldEvent",
            "AnomalyEvent", "IncidentEvent", "Health",
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
            bool gameConditionDup = false,
            bool batched = false,
            bool deathDescription = false,
            string firstId = "P1",
            string secondId = null)
        {
            return new TaleEventData
            {
                PawnId = firstId,
                Tick = 0,
                DefName = defName,
                FirstPawnId = firstId,
                SecondPawnId = secondId ?? (secondEligible ? "P2" : null),
                FirstEligible = firstEligible,
                SecondEligible = secondEligible,
                IsCoveredElsewhere = coveredElsewhere,
                IsGameConditionDuplicate = gameConditionDup,
                IsBatched = batched,
                IsDeathDescription = deathDescription,
            };
        }

        private static HediffEventData Hediff(
            string defName = "Wound",
            bool policyRecordsSource = true,
            bool modeRecordable = true,
            bool passesPolicy = true,
            string modeToken = "Immediate")
        {
            return new HediffEventData
            {
                PawnId = "P",
                Tick = 0,
                DefName = defName,
                Label = "test label",
                SourceToken = "add",
                GroupKey = "hediff_test",
                ModeToken = modeToken,
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
            bool isSignificant = true,
            bool routeToBatch = false,
            bool routeToAmbient = false)
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
                RouteToBatch = routeToBatch,
                RouteToAmbient = routeToAmbient,
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

        private static RaidEventData Raid(
            string defName = "RaidEnemy",
            string label = "enemy raid",
            string factionDefName = "Pirate",
            string points = "350")
        {
            return new RaidEventData
            {
                PawnId = "P1",
                Tick = 0,
                DefName = defName,
                Label = label,
                FactionDefName = factionDefName,
                Points = points,
            };
        }

        private static QuestEventData Quest(string signal)
        {
            return new QuestEventData
            {
                PawnId = "P1",
                Tick = 0,
                Signal = signal,
                DefName = "OpportunityQuest_Friendlies",
                Label = "A Stolen Cache",
                FactionDefName = "Outlander",
                Rewards = "Silver x100, Medicine x5",
            };
        }

        private static RitualEventData Ritual(
            string defName = "Ritual_Speech",
            string perspective = RitualEventData.PerspectiveOrganizer,
            bool cancelled = false)
        {
            return new RitualEventData
            {
                PawnId = "P1",
                Tick = 0,
                DefName = defName,
                Title = "Leader's address",
                BehaviorClass = "RitualBehaviorWorker_LeaderSpeech",
                Perspective = perspective,
                RitualRole = "author (speaker)",
                Cancelled = cancelled,
            };
        }

        private static AbilityEventData Ability(
            string defName = "Stun",
            float chance = 1f,
            float roll = 0f)
        {
            return new AbilityEventData
            {
                PawnId = "P1",
                Tick = 0,
                DefName = defName,
                Label = "stun",
                Category = "Psycast",
                TargetLabel = "Bob",
                CooldownTicks = 600,
                RecordChance = chance,
                Roll = roll,
            };
        }

        private static ArrivalEventData Arrival(
            string defName = ArrivalEventData.DefNameToken, bool existing = false)
        {
            return new ArrivalEventData
            {
                PawnId = "P1",
                Tick = 0,
                DefName = defName,
                PawnLabel = "Alice",
                PawnLoadId = "P1",
                ArrivalContext = "arrival_source=joined",
                HasExistingArrival = existing,
            };
        }

        private static DeathEventData Death(
            string defName = DeathEventData.DefNameToken, bool existing = false)
        {
            return new DeathEventData
            {
                PawnId = "P1",
                Tick = 0,
                DefName = defName,
                Label = "death",
                PawnLabel = "Alice",
                PawnLoadId = "P1",
                DeathFacts = "killer=Bear",
                HasExistingDeathDescription = existing,
            };
        }

        private static WorkEventData Work(
            string defName = WorkEventData.RoutineDefName,
            bool hasCurrentWork = true,
            bool ignored = false,
            bool cooldownClear = true,
            bool passedChance = true)
        {
            return new WorkEventData
            {
                PawnId = "P1",
                Tick = 0,
                DefName = defName,
                WorkTypeDefName = "Cooking",
                WorkGiverDefName = "DoBill",
                MoodImpact = MoodNeutral,
                HasCurrentWork = hasCurrentWork,
                IgnoredWorkType = ignored,
                SameWorkCooldownClear = cooldownClear,
                PassedChanceRoll = passedChance,
            };
        }

        private static ThoughtProgressionEventData ThoughtProgression(
            string defName = "NeedOutdoors",
            bool worsened = true,
            bool stageRecorded = false)
        {
            return new ThoughtProgressionEventData
            {
                PawnId = "P1",
                Tick = 0,
                DefName = defName,
                CategoryKey = "need_outdoors",
                Label = "stuck indoors",
                StageIndex = "2",
                Severity = "0.75",
                MoodImpact = MoodNegative,
                MoodOffset = "-8.0",
                Worsened = worsened,
                StageAlreadyRecorded = stageRecorded,
            };
        }

        private static DayReflectionEventData DayReflection(
            string defName = DayReflectionEventData.DefNameToken,
            int candidates = 3,
            int highlights = 2,
            int importantCandidates = 2,
            bool alreadyWritten = false)
        {
            return new DayReflectionEventData
            {
                PawnId = "P1",
                Tick = 0,
                DefName = defName,
                Day = 42,
                CandidateCount = candidates,
                ImportantCandidateCount = importantCandidates,
                HighlightCount = highlights,
                FillerMomentCount = 1,
                SignalTags = "thought,work",
                AlreadyWritten = alreadyWritten,
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

        private static void AssertNearlyEqual(string name, float expected, float actual, float tolerance = 0.0001f)
        {
            assertions++;
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected: [" + expected + "]\nActual:   [" + actual + "]");
            }
        }
    }
}

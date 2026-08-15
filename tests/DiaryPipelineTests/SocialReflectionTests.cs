// Standalone, no-RimWorld checks for Quality Wave H7 social-reflection policy. These tests pin the
// deterministic chance/delay boundaries, unordered pair and directional relation identities,
// admission/cooldown/replacement decisions, pre-registration reservation release, excerpt limits,
// and qualitative subject formatting. Linking the two production files directly into this project
// makes an accidental Verse, Unity, DefDatabase, Translate, or shared-Rand dependency fail at build
// time.
using System;
using System.Collections.Generic;

using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestSocialReflectionPolicy()
        {
            TestSocialReflectionChanceBoundaries();
            TestSocialReflectionChanceCoverage();
            TestSocialReflectionDeterminismAndDelay();
            TestSocialReflectionPairAndRelationIdentity();
            TestSocialReflectionAdmissionAndReplacement();
            TestSocialReflectionReservationRelease();
            TestSocialReflectionBeautyAndPolarity();
            TestSocialReflectionSourceExcerpt();
            TestSocialReflectionContextFormatting();
        }

        private static void TestSocialReflectionChanceBoundaries()
        {
            SocialReflectionPolicySnapshot policy = SocialReflectionPolicySnapshot.CreateDefault();
            var cases = new[]
            {
                new { opinion = 0, chance = 0.05f },
                new { opinion = 24, chance = 0.05f },
                new { opinion = -24, chance = 0.05f },
                new { opinion = 25, chance = 0.15f },
                new { opinion = -49, chance = 0.15f },
                new { opinion = 50, chance = 0.35f },
                new { opinion = -74, chance = 0.35f },
                new { opinion = 75, chance = 0.60f },
                new { opinion = -100, chance = 0.60f },
                // Modded opinions outside RimWorld's ordinary range clamp to the terminal band.
                new { opinion = 500, chance = 0.60f },
                new { opinion = int.MinValue, chance = 0.60f }
            };

            for (int index = 0; index < cases.Length; index++)
            {
                float actual = SocialReflectionPolicy.ChanceForOpinion(
                    cases[index].opinion,
                    policy.chanceBands);
                AssertTrue(
                    "social reflection chance case " + index,
                    Math.Abs(actual - cases[index].chance) < 0.000001f);
            }

            AssertTrue(
                "social reflection absent chance band fails closed",
                Math.Abs(SocialReflectionPolicy.ChanceForOpinion(
                    25,
                    new List<SocialReflectionChanceBand>()) - 0f) < 0.000001f);

            List<SocialReflectionChanceBand> always = new List<SocialReflectionChanceBand>
            {
                new SocialReflectionChanceBand
                {
                    minimumAbsoluteOpinion = 0,
                    maximumAbsoluteOpinion = 100,
                    chance = 1f
                }
            };
            List<SocialReflectionChanceBand> never = new List<SocialReflectionChanceBand>
            {
                new SocialReflectionChanceBand
                {
                    minimumAbsoluteOpinion = 0,
                    maximumAbsoluteOpinion = 100,
                    chance = 0f
                }
            };
            AssertTrue(
                "social reflection one chance always passes",
                SocialReflectionPolicy.PassesChance("stable-source", 0, always));
            AssertTrue(
                "social reflection zero chance never passes",
                !SocialReflectionPolicy.PassesChance("stable-source", 100, never));
            AssertTrue(
                "zero frequency multiplier closes an otherwise certain social reflection",
                !SocialReflectionPolicy.PassesChance("stable-source", 0, always, 0f));
            AssertTrue(
                "frequency multiplier preserves the deterministic strict boundary",
                SocialReflectionPolicy.PassesChance("stable-source", 0, always, 0.25f)
                    == (SocialReflectionPolicy.DeterministicRoll("stable-source") < 0.25d));
            AssertTrue(
                "increased frequency clamps an otherwise certain social reflection at one",
                SocialReflectionPolicy.PassesChance("stable-source", 0, always, 5f));
            AssertTrue(
                "frequency cannot invent a reflection from a zero native chance",
                !SocialReflectionPolicy.PassesChance("stable-source", 0, never, 5f));
            AssertTrue(
                "corrupt frequency multiplier falls back to Standard behavior",
                SocialReflectionPolicy.PassesChance(
                    "stable-source", 0, always, float.NaN)
                    == SocialReflectionPolicy.PassesChance("stable-source", 0, always));
        }

        private static void TestSocialReflectionChanceCoverage()
        {
            List<SocialReflectionChanceBand> valid =
                SocialReflectionPolicySnapshot.CreateDefault().chanceBands;
            AssertTrue(
                "social reflection default chance bands cover zero through one hundred",
                SocialReflectionPolicy.HasCompleteChanceCoverage(valid));
            AssertTrue(
                "social reflection reordered complete chance bands remain valid",
                SocialReflectionPolicy.HasCompleteChanceCoverage(
                    new List<SocialReflectionChanceBand>
                    {
                        ChanceBand(75, 100, 0.60f),
                        ChanceBand(0, 24, 0.05f),
                        ChanceBand(50, 74, 0.35f),
                        ChanceBand(25, 49, 0.15f)
                    }));
            AssertTrue(
                "social reflection chance bands reject a coverage gap",
                !SocialReflectionPolicy.HasCompleteChanceCoverage(
                    new List<SocialReflectionChanceBand>
                    {
                        ChanceBand(0, 24, 0.05f),
                        ChanceBand(26, 100, 0.60f)
                    }));
            AssertTrue(
                "social reflection chance bands reject an overlap",
                !SocialReflectionPolicy.HasCompleteChanceCoverage(
                    new List<SocialReflectionChanceBand>
                    {
                        ChanceBand(0, 50, 0.05f),
                        ChanceBand(50, 100, 0.60f)
                    }));
            AssertTrue(
                "social reflection chance bands reject partial coverage",
                !SocialReflectionPolicy.HasCompleteChanceCoverage(
                    new List<SocialReflectionChanceBand>
                    {
                        ChanceBand(25, 100, 0.60f)
                    }));
            AssertTrue(
                "social reflection chance bands reject invalid probability",
                !SocialReflectionPolicy.HasCompleteChanceCoverage(
                    new List<SocialReflectionChanceBand>
                    {
                        ChanceBand(0, 100, 1.5f)
                    }));
            AssertTrue(
                "social reflection chance bands reject null rows",
                !SocialReflectionPolicy.HasCompleteChanceCoverage(
                    new List<SocialReflectionChanceBand>
                    {
                        ChanceBand(0, 49, 0.15f),
                        null,
                        ChanceBand(50, 100, 0.60f)
                    }));
        }

        private static void TestSocialReflectionDeterminismAndDelay()
        {
            string sourceKey = SocialReflectionPolicy.SourceKey(
                "play-log-7",
                12345,
                "DeepTalk",
                "Pawn_1",
                "Pawn_2");
            AssertEqual(
                "social reflection source identity is stable",
                sourceKey,
                SocialReflectionPolicy.SourceKey(
                    "play-log-7",
                    12345,
                    "DeepTalk",
                    "Pawn_1",
                    "Pawn_2"));
            AssertTrue(
                "social reflection length-prefixed source identity separates ambiguous segments",
                SocialReflectionPolicy.SourceKey("ab", 1, "c", "d", "e")
                    != SocialReflectionPolicy.SourceKey("a", 1, "bc", "d", "e"));

            double firstRoll = SocialReflectionPolicy.DeterministicRoll(sourceKey);
            double secondRoll = SocialReflectionPolicy.DeterministicRoll(sourceKey);
            AssertTrue(
                "social reflection roll is stable and inside the unit interval",
                firstRoll == secondRoll && firstRoll >= 0d && firstRoll < 1d);

            int firstDelay = SocialReflectionPolicy.DelayTicks(sourceKey, 15000, 60000);
            AssertEqual(
                "social reflection delay is stable",
                firstDelay,
                SocialReflectionPolicy.DelayTicks(sourceKey, 15000, 60000));
            AssertTrue(
                "social reflection delay stays in the inclusive six-to-twenty-four-hour window",
                firstDelay >= 15000 && firstDelay <= 60000);

            HashSet<double> sampledRolls = new HashSet<double>();
            HashSet<int> sampledDelays = new HashSet<int>();
            bool sawIntermediatePass = false;
            bool sawIntermediateFailure = false;
            List<SocialReflectionChanceBand> halfChance =
                new List<SocialReflectionChanceBand>
                {
                    ChanceBand(0, 100, 0.5f)
                };
            for (int index = 0; index < 64; index++)
            {
                string sampledSource = "source-" + index;
                sampledRolls.Add(SocialReflectionPolicy.DeterministicRoll(sampledSource));
                int delay = SocialReflectionPolicy.DelayTicks(
                    sampledSource,
                    15000,
                    60000);
                sampledDelays.Add(delay);
                if (SocialReflectionPolicy.PassesChance(sampledSource, 0, halfChance))
                {
                    sawIntermediatePass = true;
                }
                else
                {
                    sawIntermediateFailure = true;
                }
                AssertTrue(
                    "social reflection delay range sample " + index,
                    delay >= 15000 && delay <= 60000);
            }
            AssertTrue(
                "social reflection deterministic rolls vary across source identities",
                sampledRolls.Count > 1);
            AssertTrue(
                "social reflection deterministic delays vary across source identities",
                sampledDelays.Count > 1);
            AssertTrue(
                "social reflection intermediate chance produces both outcomes",
                sawIntermediatePass && sawIntermediateFailure);

            AssertEqual(
                "social reflection malformed delay window collapses to safe minimum",
                100,
                SocialReflectionPolicy.DelayTicks("malformed", 100, 50));
        }

        private static void TestSocialReflectionPairAndRelationIdentity()
        {
            string forwardPair = SocialReflectionPolicy.PairKey("Pawn_2", "Pawn_1");
            string reversePair = SocialReflectionPolicy.PairKey("Pawn_1", "Pawn_2");
            AssertEqual("social reflection pair key is unordered", forwardPair, reversePair);
            AssertEqual(
                "social reflection equal pawn cannot form pair",
                string.Empty,
                SocialReflectionPolicy.PairKey("Pawn_1", "Pawn_1"));
            AssertEqual(
                "social reflection blank pawn cannot form pair",
                string.Empty,
                SocialReflectionPolicy.PairKey("Pawn_1", " "));
            AssertTrue("length-prefixed pair parser finds either exact pawn",
                SocialReflectionPolicy.PairKeyContainsPawn(forwardPair, "Pawn_1")
                    && SocialReflectionPolicy.PairKeyContainsPawn(forwardPair, "Pawn_2"));
            AssertTrue("length-prefixed pair parser rejects prefix collisions",
                !SocialReflectionPolicy.PairKeyContainsPawn(
                    SocialReflectionPolicy.PairKey("Pawn_10", "Pawn_2"), "Pawn_1"));
            AssertTrue("length-prefixed pair parser rejects malformed and trailing data",
                !SocialReflectionPolicy.PairKeyContainsPawn("6:Pawn_1", "Pawn_1")
                    && !SocialReflectionPolicy.PairKeyContainsPawn(
                        forwardPair + "junk", "Pawn_1")
                    && !SocialReflectionPolicy.PairKeyContainsPawn(
                        "x:Pawn_14:Pawn", "Pawn_1")
                    && !SocialReflectionPolicy.PairKeyContainsPawn(forwardPair, " "));

            List<SocialReflectionRelationFact> facts = new List<SocialReflectionRelationFact>
            {
                Relation("Pawn_1", "Pawn_2", "Parent"),
                Relation("Pawn_2", "Pawn_1", "Child"),
                // Duplicate and unrelated rows cannot perturb the canonical signature.
                Relation("Pawn_1", "Pawn_2", "Parent"),
                Relation("Pawn_1", "Pawn_9", "Friend")
            };
            List<SocialReflectionRelationFact> reordered = new List<SocialReflectionRelationFact>
            {
                Relation("Pawn_2", "Pawn_1", "Child"),
                Relation("Pawn_1", "Pawn_2", "Parent")
            };
            string signature = SocialReflectionPolicy.RelationSignature(
                "Pawn_1",
                "Pawn_2",
                facts);
            AssertEqual(
                "social reflection relation signature ignores enumeration order and duplicates",
                signature,
                SocialReflectionPolicy.RelationSignature("Pawn_2", "Pawn_1", reordered));

            reordered[0].relationDefName = "Sibling";
            AssertTrue(
                "social reflection relation signature preserves directional complete relation set",
                signature != SocialReflectionPolicy.RelationSignature(
                    "Pawn_1",
                    "Pawn_2",
                    reordered));
        }

        private static void TestSocialReflectionAdmissionAndReplacement()
        {
            const int now = 1000;
            SocialReflectionAdmissionInput input = Admission(
                "source-new",
                "Pawn_1",
                "Pawn_2",
                "relation-v1",
                now);
            SocialReflectionAdmissionDecision decision =
                SocialReflectionPolicy.DecideAdmission(input);
            AssertTrue("social reflection clear candidate is admitted", decision.accepted);
            AssertTrue(
                "social reflection ordinary admission replaces nothing",
                !decision.replacePending && !decision.clearPairCooldown);
            AssertEqual(
                "social reflection admission owns unordered pair key",
                SocialReflectionPolicy.PairKey("Pawn_1", "Pawn_2"),
                decision.pairKey);

            input.pendingSourceKey = "source-old";
            input.writerHasPending = true;
            input.pendingWriterPawnId = "Pawn_1";
            input.pendingPairKey = SocialReflectionPolicy.PairKey("Pawn_1", "Pawn_9");
            decision = SocialReflectionPolicy.DecideAdmission(input);
            AssertTrue(
                "social reflection one-pending-row-per-writer gate",
                !decision.accepted && decision.rejection == "writer_pending");

            // A same-pair candidate in the reverse POV is still blocked while the relationship
            // signature is unchanged.
            input = Admission("source-new", "Pawn_2", "Pawn_1", "relation-v1", now);
            input.pendingSourceKey = "source-old";
            input.pendingWriterPawnId = "Pawn_1";
            input.pendingPairKey = SocialReflectionPolicy.PairKey("Pawn_1", "Pawn_2");
            input.pairPendingWriterPawnId = "Pawn_1";
            input.pendingRelationSignature = "relation-v1";
            decision = SocialReflectionPolicy.DecideAdmission(input);
            AssertTrue(
                "social reflection unchanged reverse pair remains pending",
                !decision.accepted
                    && !decision.replacePending
                    && decision.rejection == "pair_pending");

            // A real direct-relation change permits the one narrow replacement: reverse POV, newer
            // source, same pair. The adapter can now release the old writer's reservation.
            input.relationSignature = "relation-v2";
            input.writerHasPending = true;
            decision = SocialReflectionPolicy.DecideAdmission(input);
            AssertTrue(
                "social reflection writer pending elsewhere blocks reverse pair replacement",
                !decision.accepted
                    && !decision.replacePending
                    && decision.rejection == "writer_pending");

            input.writerHasPending = false;
            decision = SocialReflectionPolicy.DecideAdmission(input);
            AssertTrue(
                "social reflection changed relation permits reverse pending replacement",
                decision.accepted
                    && decision.replacePending
                    && decision.clearPairCooldown
                    && decision.rejection.Length == 0);

            input = Admission("source-new", "Pawn_1", "Pawn_2", "relation-v1", now);
            input.pairCooldownUntilTick = now + 100;
            input.pairCooldownRelationSignature = "relation-v1";
            decision = SocialReflectionPolicy.DecideAdmission(input);
            AssertTrue(
                "social reflection unchanged relation respects pair cooldown",
                !decision.accepted && decision.rejection == "pair_cooldown");

            input.relationSignature = "relation-v2";
            decision = SocialReflectionPolicy.DecideAdmission(input);
            AssertTrue(
                "social reflection changed relation clears only pair cooldown",
                decision.accepted && decision.clearPairCooldown);

            // Writer pacing remains authoritative even when the relation change invalidates the pair
            // reservation. The clear flag lets the adapter discard only that stale pair row.
            input.writerCooldownUntilTick = now + 200;
            decision = SocialReflectionPolicy.DecideAdmission(input);
            AssertTrue(
                "social reflection relation reset preserves writer cooldown",
                !decision.accepted
                    && decision.clearPairCooldown
                    && decision.rejection == "writer_cooldown");
        }

        private static void TestSocialReflectionReservationRelease()
        {
            AssertTrue(
                "social reflection pre-registration cancellation releases owned reservation",
                SocialReflectionPolicy.ReleasesReservation(
                    "source-1",
                    "source-1",
                    eventWasRegistered: false));
            AssertTrue(
                "social reflection cancellation cannot release another source reservation",
                !SocialReflectionPolicy.ReleasesReservation(
                    "source-1",
                    "source-2",
                    eventWasRegistered: false));
            AssertTrue(
                "social reflection registered page retains reservation after generation failure",
                !SocialReflectionPolicy.ReleasesReservation(
                    "source-1",
                    "source-1",
                    eventWasRegistered: true));
            AssertTrue(
                "social reflection blank source cannot release reservation",
                !SocialReflectionPolicy.ReleasesReservation(
                    " ",
                    " ",
                    eventWasRegistered: false));
        }

        private static void TestSocialReflectionBeautyAndPolarity()
        {
            SocialReflectionPolicySnapshot policy = SocialReflectionPolicySnapshot.CreateDefault();
            var beautyCases = new[]
            {
                new { score = -2f, band = 0 },
                new { score = -1.5f, band = 0 },
                new { score = -1.499f, band = 1 },
                new { score = -0.5f, band = 1 },
                new { score = -0.499f, band = 2 },
                new { score = 0.499f, band = 2 },
                new { score = 0.5f, band = 3 },
                new { score = 1.499f, band = 3 },
                new { score = 1.5f, band = 4 },
                new { score = 2f, band = 4 },
                new { score = float.NaN, band = 2 },
                new { score = float.PositiveInfinity, band = 2 }
            };
            for (int index = 0; index < beautyCases.Length; index++)
            {
                AssertEqual(
                    "social reflection beauty boundary " + index,
                    beautyCases[index].band,
                    SocialReflectionPolicy.BeautyBandIndex(
                        beautyCases[index].score,
                        policy));
            }

            AssertEqual(
                "social reflection negative polarity boundary",
                SocialReflectionPolicy.NegativePolarity,
                SocialReflectionPolicy.PolarityForOpinion(-1, -1, 1));
            AssertEqual(
                "social reflection neutral polarity boundary",
                SocialReflectionPolicy.NeutralPolarity,
                SocialReflectionPolicy.PolarityForOpinion(0, -1, 1));
            AssertEqual(
                "social reflection positive polarity boundary",
                SocialReflectionPolicy.PositivePolarity,
                SocialReflectionPolicy.PolarityForOpinion(1, -1, 1));
        }

        private static void TestSocialReflectionSourceExcerpt()
        {
            AssertEqual(
                "social reflection excerpt keeps final two sentences",
                "Second sentence! Third sentence?",
                SocialReflectionPolicy.SourceExcerpt(
                    "First sentence. Second sentence! Third sentence?",
                    2,
                    400));
            AssertEqual(
                "social reflection excerpt zero sentence cap is empty",
                string.Empty,
                SocialReflectionPolicy.SourceExcerpt("One. Two.", 0, 400));
            AssertEqual(
                "social reflection excerpt zero character cap is empty",
                string.Empty,
                SocialReflectionPolicy.SourceExcerpt("One. Two.", 2, 0));
            AssertEqual(
                "social reflection excerpt keeps an ellipsis as one terminator",
                "I waited...",
                SocialReflectionPolicy.SourceExcerpt(
                    "Earlier thought. I waited...",
                    1,
                    400));
            AssertEqual(
                "social reflection excerpt keeps abbreviations inside a sentence",
                "I met Dr. Smith. We talked.",
                SocialReflectionPolicy.SourceExcerpt(
                    "I met Dr. Smith. We talked.",
                    2,
                    400));
            AssertEqual(
                "social reflection excerpt keeps decimal points inside a sentence",
                "The reading was 3.5 volts. It held.",
                SocialReflectionPolicy.SourceExcerpt(
                    "The reading was 3.5 volts. It held.",
                    2,
                    400));
            AssertEqual(
                "social reflection excerpt includes a closing quote with its sentence",
                "\"We talked.\" Then I left.",
                SocialReflectionPolicy.SourceExcerpt(
                    "Earlier. \"We talked.\" Then I left.",
                    2,
                    400));

            string longExcerpt = SocialReflectionPolicy.SourceExcerpt(
                new string('A', 250) + ". " + new string('B', 250) + "?",
                2,
                400);
            AssertEqual("social reflection excerpt hard character cap", 400, longExcerpt.Length);
            AssertTrue(
                "social reflection excerpt keeps newest prose at the hard cap",
                longExcerpt.EndsWith("?", StringComparison.Ordinal)
                    && longExcerpt.IndexOf(new string('B', 100), StringComparison.Ordinal) >= 0);

            string emojiExcerpt = SocialReflectionPolicy.SourceExcerpt(
                "Earlier. " + new string('x', 20) + "\U0001F600",
                1,
                1);
            AssertTrue(
                "social reflection excerpt never leaves an unpaired UTF-16 surrogate",
                emojiExcerpt.Length == 0 || !char.IsLowSurrogate(emojiExcerpt[0]));
        }

        private static void TestSocialReflectionContextFormatting()
        {
            SocialReflectionPolicySnapshot policy = SocialReflectionPolicySnapshot.CreateDefault();
            policy.veryUnattractiveLabel = "very unattractive";
            policy.unattractiveLabel = "unattractive";
            policy.ordinaryAppearanceLabel = "ordinary";
            policy.attractiveLabel = "attractive";
            policy.veryAttractiveLabel = "very attractive";
            policy.maximumOpinionReasons = 3;
            policy.styles = new List<SocialReflectionStyle>
            {
                new SocialReflectionStyle
                {
                    polarity = SocialReflectionPolicy.PositivePolarity,
                    tone = "warm tone",
                    colorCue = "warm cue"
                },
                new SocialReflectionStyle
                {
                    polarity = SocialReflectionPolicy.NeutralPolarity,
                    tone = "quiet tone",
                    colorCue = "quiet cue"
                },
                new SocialReflectionStyle
                {
                    polarity = SocialReflectionPolicy.NegativePolarity,
                    tone = "tense tone",
                    colorCue = "tense cue"
                }
            };
            SocialReflectionSubjectFacts facts = new SocialReflectionSubjectFacts
            {
                displayName = "  Mira  ",
                biologicalAgeYears = 27,
                lifeStageLabel = " adult ",
                beauty = 1.75f,
                primaryRelationLabel = " wife ",
                opinion = 83,
                opinionBandLabel = " warmly attached ",
                socialReasons = new List<string>
                {
                    " rescued me ",
                    "shared feast",
                    "rescued me",
                    " ",
                    "trusted counsel",
                    "over cap"
                }
            };

            SocialReflectionFormattedContext formatted =
                SocialReflectionContextFormatter.Format(facts, policy);
            AssertEqual("social reflection formatted subject name", "Mira", formatted.subjectName);
            AssertEqual("social reflection formatted age", "27", formatted.subjectAge);
            AssertEqual(
                "social reflection formatted life stage",
                "adult",
                formatted.subjectLifeStage);
            AssertEqual(
                "social reflection formatted beauty label",
                "very attractive",
                formatted.subjectAppearance);
            AssertEqual(
                "social reflection formatted relation",
                "wife",
                formatted.subjectRelation);
            AssertEqual(
                "social reflection formatted sentiment",
                "warmly attached",
                formatted.subjectSentiment);
            AssertEqual(
                "social reflection formatted reason cap and dedup",
                "rescued me, shared feast, trusted counsel",
                formatted.subjectReasons);
            AssertEqual(
                "social reflection formatted polarity",
                SocialReflectionPolicy.PositivePolarity,
                formatted.polarity);
            AssertEqual("social reflection formatted tone", "warm tone", formatted.tone);
            AssertEqual("social reflection formatted cue", "warm cue", formatted.colorCue);

            string allPromptFields = string.Join(
                "|",
                new[]
                {
                    formatted.subjectName,
                    formatted.subjectAge,
                    formatted.subjectLifeStage,
                    formatted.subjectAppearance,
                    formatted.subjectRelation,
                    formatted.subjectSentiment,
                    formatted.subjectReasons,
                    formatted.polarity,
                    formatted.tone,
                    formatted.colorCue
                });
            AssertTrue(
                "social reflection formatter never leaks raw opinion or beauty scores",
                allPromptFields.IndexOf("83", StringComparison.Ordinal) < 0
                    && allPromptFields.IndexOf("1.75", StringComparison.Ordinal) < 0);

            SocialReflectionFormattedContext sparse =
                SocialReflectionContextFormatter.Format(
                    new SocialReflectionSubjectFacts
                    {
                        displayName = "Niko",
                        biologicalAgeYears = 19,
                        lifeStageLabel = "adult",
                        beauty = 0f,
                        opinion = 0
                    },
                    policy);
            AssertEqual(
                "social reflection missing relation stays omitted",
                string.Empty,
                sparse.subjectRelation);
            AssertEqual(
                "social reflection missing sentiment stays omitted",
                string.Empty,
                sparse.subjectSentiment);
            AssertEqual(
                "social reflection missing reasons stay omitted",
                string.Empty,
                sparse.subjectReasons);
            SocialReflectionFormattedContext missingAge =
                SocialReflectionContextFormatter.Format(
                    new SocialReflectionSubjectFacts
                    {
                        displayName = "Tara",
                        biologicalAgeYears = null
                    },
                    policy);
            AssertEqual(
                "social reflection missing age stays omitted",
                string.Empty,
                missingAge.subjectAge);
            AssertEqual(
                "social reflection missing appearance stays omitted",
                string.Empty,
                missingAge.subjectAppearance);
        }

        private static SocialReflectionRelationFact Relation(
            string ownerPawnId,
            string otherPawnId,
            string relationDefName)
        {
            return new SocialReflectionRelationFact
            {
                ownerPawnId = ownerPawnId,
                otherPawnId = otherPawnId,
                relationDefName = relationDefName
            };
        }

        private static SocialReflectionChanceBand ChanceBand(
            int minimum,
            int maximum,
            float chance)
        {
            return new SocialReflectionChanceBand
            {
                minimumAbsoluteOpinion = minimum,
                maximumAbsoluteOpinion = maximum,
                chance = chance
            };
        }

        private static SocialReflectionAdmissionInput Admission(
            string sourceKey,
            string writerPawnId,
            string subjectPawnId,
            string relationSignature,
            int nowTick)
        {
            return new SocialReflectionAdmissionInput
            {
                sourceKey = sourceKey,
                writerPawnId = writerPawnId,
                subjectPawnId = subjectPawnId,
                relationSignature = relationSignature,
                nowTick = nowTick
            };
        }
    }
}

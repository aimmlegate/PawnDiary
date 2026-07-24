// In-game smoke tests for Pawn Diary's XML Def registration. RimTest Redux discovers this static
// suite by reflection after RimWorld has loaded all active mods. These tests deliberately inspect
// read-only Def data: they need no colony, create no pawns, and leave the current save untouched.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Checks a few foundational Def contracts that only the real RimWorld loader can prove.
    /// Standalone tests cover pure helpers; this suite catches missing or malformed runtime XML.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryDefSmokeTests
    {
        /// <summary>
        /// Verifies that the singleton policy Defs used throughout Pawn Diary registered by name.
        /// </summary>
        [Test]
        public static void CoreSingletonDefsAreLoaded()
        {
            RequireDef<DiaryTuningDef>("Diary_Tuning");
            RequireDef<DiaryPromptDef>("Diary_Prompts");
            RequireDef<DiaryUiStyleDef>("Diary_UiStyle");
            RequireDef<DiaryContextDetailDef>("Diary_ContextDetail");
            RequireDef<DiaryRoyaltyPolicyDef>("Diary_Royalty");
            DiaryAnomalyPolicyDef anomaly =
                RequireDef<DiaryAnomalyPolicyDef>("Diary_AnomalyPolicy");
            // A1.0 deliberately ships this policy in every profile. These read-only assertions prove
            // the real RimWorld XML loader retained its conservative primitive-only defaults.
            Assert.That(anomaly.studyEnabled && anomaly.recordFirstStudyBreakthrough
                && anomaly.recordCompletedEntityKind);
            Assert.That(anomaly.promotedStudyMilestones != null
                && anomaly.promotedStudyMilestones.Count == 0);
            Assert.That(anomaly.containmentEnabled && anomaly.containmentWitnessRadius == 12
                && anomaly.containmentMaxWriters == 2
                && anomaly.containmentMaxEntityLabelsInContext == 3);
            Assert.That(anomaly.studyTaleSuppressionTicks == 2500
                && anomaly.taleOwnershipMaxDepth == 8);
            AnomalyPolicySnapshot anomalySnapshot = DiaryAnomalyPolicy.Snapshot();
            Assert.That(anomalySnapshot.studyEnabled
                && anomalySnapshot.containmentWitnessRadius == 12
                && anomalySnapshot.containmentMaxWriters == 2
                && anomalySnapshot.promotedStudyMilestones.Count == 0);
            Assert.That(!ReferenceEquals(anomalySnapshot, DiaryAnomalyPolicy.Snapshot()));
            RequireDef<DiaryKnowledgeTuningDef>("Diary_Knowledge");
            RequireDef<DiaryImportantEventDef>("Diary_ImpEvent_Married");
            RequireDef<DiaryCultureTopicDef>("Diary_CultureTopic_Mechanoids");
            RequireDef<DiaryCultureProfileDef>("Diary_CultureProfile_Astropolitan");
        }

        /// <summary>
        /// Verifies that representative base-game-safe interaction groups are available.
        /// </summary>
        [Test]
        public static void RequiredBaseInteractionGroupsAreLoaded()
        {
            RequireDef<DiaryInteractionGroupDef>("smalltalk");
            RequireDef<DiaryInteractionGroupDef>("socialfight");
            RequireDef<DiaryInteractionGroupDef>("mentalbreak");
            RequireDef<DiaryInteractionGroupDef>("arrival");
            RequireDef<DiaryInteractionGroupDef>("other");
        }

        /// <summary>
        /// Verifies that Phase 7's Royal Ascent Def family loaded and root-first Quest routing owns
        /// only the exact Royalty quest. Package gates decide runtime availability separately.
        /// </summary>
        [Test]
        public static void RoyalAscentDefsAndExactQuestRouteAreLoaded()
        {
            DiaryInteractionGroupDef ascent =
                RequireDef<DiaryInteractionGroupDef>("questRoyalAscent");
            RequireDef<DiaryEventWindowDef>("RoyalAscent");
            RequireDef<DiaryEventPromptDef>("DiaryEventPrompt_RoyalAscent");

            Assert.That(
                InteractionGroups.ClassifyQuest("EndGame_RoyalAscent", "completed") == ascent);
            Assert.That(
                InteractionGroups.ClassifyQuest("PawnDiaryTest_OrdinaryQuest", "completed")
                != ascent);
        }

        /// <summary>
        /// Verifies that the Phase A1.1 Anomaly event families load behind the official package gate
        /// and that their required-match classifier never falls through to a broad Interaction row.
        /// </summary>
        [Test]
        public static void AnomalyEventGroupsAreLoadedAndRouteOnlyExactKinds()
        {
            string[,] expected =
            {
                { "anomalyStudyBreakthrough", "PawnDiary_AnomalyStudyBreakthrough" },
                { "anomalyContainmentBreach", "PawnDiary_ContainmentBreach" },
                { "anomalyCreepJoinerOutcome", "PawnDiary_CreepJoinerOutcome" },
                { "anomalyGhoulTransformation", "PawnDiary_GhoulTransformation" },
                { "anomalyVoidOutcome", "PawnDiary_VoidOutcome" }
            };

            for (int i = 0; i < expected.GetLength(0); i++)
            {
                DiaryInteractionGroupDef group =
                    RequireDef<DiaryInteractionGroupDef>(expected[i, 0]);
                Assert.That(group.enableWhenPackageIdsLoaded != null
                    && group.enableWhenPackageIdsLoaded.Count == 1
                    && group.enableWhenPackageIdsLoaded[0] == "Ludeon.RimWorld.Anomaly");
                Assert.That(group.MissingRequiredPackage() == !ModsConfig.AnomalyActive);

                DiaryInteractionGroupDef classified =
                    InteractionGroups.ClassifyAnomalyEvent(expected[i, 1]);
                Assert.That(ModsConfig.AnomalyActive ? classified == group : classified == null);
            }

            Assert.That(InteractionGroups.ClassifyAnomalyEvent(
                "PawnDiary_UnknownAnomalyEvent") == null);
        }

        /// <summary>
        /// Verifies that every loaded prompt template has a stable unique key and usable fields.
        /// </summary>
        [Test]
        public static void PromptTemplatesHaveUniqueKeysAndFields()
        {
            List<DiaryPromptTemplateDef> templates =
                DefDatabase<DiaryPromptTemplateDef>.AllDefsListForReading;

            Assert.That(templates.Count).Is.GreaterThan(0);

            HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < templates.Count; i++)
            {
                DiaryPromptTemplateDef template = templates[i];
                if (template == null)
                {
                    throw new AssertionException("Pawn Diary loaded a null prompt template Def.");
                }

                if (string.IsNullOrWhiteSpace(template.templateKey))
                {
                    throw new AssertionException(
                        "Prompt template '" + template.defName + "' has no templateKey.");
                }

                if (!seenKeys.Add(template.templateKey))
                {
                    throw new AssertionException(
                        "Duplicate Pawn Diary prompt templateKey: " + template.templateKey);
                }

                if (template.fields == null || template.fields.Count == 0)
                {
                    throw new AssertionException(
                        "Prompt template '" + template.defName + "' has no fields.");
                }
            }
        }

        // GetNamedSilentFail keeps the test failure inside RimTest's result view instead of asking
        // RimWorld's DefDatabase to throw its own less-focused startup exception.
        private static TDef RequireDef<TDef>(string defName) where TDef : Def
        {
            TDef def = DefDatabase<TDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                throw new AssertionException(
                    "Required " + typeof(TDef).Name + " '" + defName + "' was not loaded.");
            }

            return def;
        }
    }
}

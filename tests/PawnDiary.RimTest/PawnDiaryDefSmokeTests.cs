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
        /// Proves RimWorld's real XML loader can instantiate all three frequency presets and retains
        /// an explicit supported tier on every shipped player-facing interaction group.
        /// </summary>
        [Test]
        public static void FrequencyPresetsAndGroupTiersAreLoaded()
        {
            DiaryFrequencyPresetDef lite =
                RequireDef<DiaryFrequencyPresetDef>(DiaryFrequencyPresets.LiteDefName);
            DiaryFrequencyPresetDef standard =
                RequireDef<DiaryFrequencyPresetDef>(DiaryFrequencyPresets.StandardDefName);
            RequireDef<DiaryFrequencyPresetDef>(DiaryFrequencyPresets.FrequentDefName);

            DiaryFrequencyPresetSnapshot liteSnapshot = DiaryFrequencyPresets.Snapshot(lite);
            DiaryFrequencyPresetSnapshot standardSnapshot = DiaryFrequencyPresets.Snapshot(standard);
            Assert.That(liteSnapshot.tierMultipliers[DiaryFrequencyTiers.Essential] == 1f);
            Assert.That(liteSnapshot.tierMultipliers[DiaryFrequencyTiers.Ambient] == 0.15f);

            // DefDatabase contains rows from every active mod, including third-party extensions of
            // our public Def type. modContentPack.PackageId is RimWorld's ownership marker; anchor
            // the filter to one required Pawn Diary Def so release/development package ids both work.
            string pawnDiaryPackageId = standard.modContentPack?.PackageId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(pawnDiaryPackageId))
            {
                throw new AssertionException(
                    "The Standard frequency preset has no owning modContentPack package id.");
            }

            List<DiaryInteractionGroupDef> groups =
                DefDatabase<DiaryInteractionGroupDef>.AllDefsListForReading;
            int controlled = 0;
            int external = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                DiaryInteractionGroupDef group = groups[i];
                string ownerPackageId = group?.modContentPack?.PackageId ?? string.Empty;
                if (!string.Equals(
                    ownerPackageId,
                    pawnDiaryPackageId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (group.domain == GroupDomain.External)
                {
                    external++;
                    continue;
                }

                controlled++;
                if (!DiaryFrequencyTiers.IsKnown(group.frequencyTier))
                {
                    throw new AssertionException(
                        "Frequency-controlled group '" + group.defName
                        + "' loaded without a supported frequencyTier.");
                }

                Assert.That(DiaryFrequencyPolicy.ResolvePresetMultiplier(
                    standardSnapshot,
                    group.defName,
                    group.frequencyTier) == 1f);
            }

            Assert.That(controlled == 147);
            Assert.That(external == 1);
        }

        /// <summary>
        /// Exercises malformed and partial frequency Defs at the impure XML-to-pure-snapshot edge.
        /// The pure policy has broader arithmetic coverage; these cases prove the RimWorld adapter
        /// trims stable keys, copies rows, ignores unknown rows, and retains safe fallback behavior.
        /// </summary>
        [Test]
        public static void FrequencyPresetAdapterHandlesPartialAndMalformedDefs()
        {
            DiaryFrequencyPresetSnapshot nullSnapshot =
                DiaryFrequencyPresets.Snapshot((DiaryFrequencyPresetDef)null);
            Assert.That(nullSnapshot.presetKey == DiaryFrequencyPresets.StandardDefName);
            Assert.That(nullSnapshot.tierMultipliers[DiaryFrequencyTiers.Essential] == 1f);
            Assert.That(nullSnapshot.tierMultipliers[DiaryFrequencyTiers.Significant] == 1f);
            Assert.That(nullSnapshot.tierMultipliers[DiaryFrequencyTiers.Routine] == 1f);
            Assert.That(nullSnapshot.tierMultipliers[DiaryFrequencyTiers.Ambient] == 1f);

            // A partially loaded third-party Def is still a real preset snapshot. Missing lists and
            // missing tier rows inherit the pure policy's Standard 1x corruption fallback.
            DiaryFrequencyPresetDef partial = new DiaryFrequencyPresetDef
            {
                defName = "PawnDiary_RimTest_PartialFrequency"
            };
            DiaryFrequencyPresetSnapshot partialSnapshot = DiaryFrequencyPresets.Snapshot(partial);
            Assert.That(partialSnapshot.presetKey == partial.defName);
            Assert.That(DiaryFrequencyPolicy.ResolvePresetMultiplier(
                partialSnapshot,
                "thirdPartyGroup",
                DiaryFrequencyTiers.Routine) == 1f);

            DiaryFrequencyPresetDef malformed = new DiaryFrequencyPresetDef
            {
                defName = "PawnDiary_RimTest_MalformedFrequency",
                tierMultipliers = new List<DiaryFrequencyTierMultiplier>
                {
                    null,
                    new DiaryFrequencyTierMultiplier { tier = " ROUTINE ", multiplier = 0.25f },
                    new DiaryFrequencyTierMultiplier { tier = "routine", multiplier = 0.75f },
                    new DiaryFrequencyTierMultiplier { tier = "SIGNIFICANT", multiplier = float.NaN },
                    new DiaryFrequencyTierMultiplier { tier = "future-tier", multiplier = 0.2f },
                    new DiaryFrequencyTierMultiplier { tier = " ", multiplier = 0.2f }
                },
                groupOverrides = new List<DiaryFrequencyGroupMultiplier>
                {
                    null,
                    new DiaryFrequencyGroupMultiplier { groupKey = " dayreflection ", multiplier = 0.4f },
                    new DiaryFrequencyGroupMultiplier { groupKey = "DAYREFLECTION", multiplier = 0.8f },
                    new DiaryFrequencyGroupMultiplier
                    {
                        groupKey = "brokenGroup",
                        multiplier = float.PositiveInfinity
                    },
                    new DiaryFrequencyGroupMultiplier { groupKey = " ", multiplier = 0.2f }
                }
            };

            DiaryFrequencyPresetSnapshot malformedSnapshot =
                DiaryFrequencyPresets.Snapshot(malformed);
            Assert.That(malformedSnapshot.tierMultipliers.Count == 2);
            Assert.That(malformedSnapshot.groupOverrides.Count == 2);
            Assert.That(DiaryFrequencyPolicy.ResolvePresetMultiplier(
                malformedSnapshot,
                "ordinaryGroup",
                "RoUtInE") == 0.25f);
            Assert.That(DiaryFrequencyPolicy.ResolvePresetMultiplier(
                malformedSnapshot,
                "DAYREFLECTION",
                DiaryFrequencyTiers.Ambient) == 0.4f);
            Assert.That(DiaryFrequencyPolicy.ResolvePresetMultiplier(
                malformedSnapshot,
                "brokenGroup",
                DiaryFrequencyTiers.Routine) == 0.25f);
            Assert.That(DiaryFrequencyPolicy.ResolvePresetMultiplier(
                malformedSnapshot,
                "ordinaryGroup",
                DiaryFrequencyTiers.Significant) == 1f);

            // Snapshot rows are detached values. Later XML-Def mutation must not reinterpret a
            // pending/runtime request that already froze its selected preset.
            malformed.tierMultipliers[1].multiplier = 0.9f;
            malformed.groupOverrides[1].multiplier = 0.9f;
            Assert.That(malformedSnapshot.tierMultipliers[DiaryFrequencyTiers.Routine] == 0.25f);
            Assert.That(malformedSnapshot.groupOverrides["dayreflection"] == 0.4f);

            DiaryFrequencyPresetSnapshot unknownSnapshot =
                DiaryFrequencyPresets.Snapshot("  PawnDiary_RimTest_MissingFrequencyPreset  ");
            Assert.That(unknownSnapshot.presetKey == DiaryFrequencyPresets.StandardDefName);
            Assert.That(DiaryFrequencyPolicy.ResolvePresetMultiplier(
                unknownSnapshot,
                "unknownGroup",
                DiaryFrequencyTiers.Ambient) == 1f);
        }

        /// <summary>
        /// Proves legacy migration inventories package-gated compatibility rows even while their
        /// target mod is absent, so old Social intent is waiting if that mod is installed later.
        /// </summary>
        [Test]
        public static void FrequencyMigrationIncludesDormantCompatibilityGroups()
        {
            using (new FrequencyPromotionPolicyFixtureScope("hospitality_guestwork"))
            {
                List<DiaryFrequencyMigrationGroupSnapshot> groups =
                    PawnDiarySettings.FrequencyMigrationGroupSnapshots();
                DiaryFrequencyMigrationGroupSnapshot hospitality = groups.Find(group =>
                    string.Equals(
                        group?.groupKey,
                        "hospitality_guestwork",
                        StringComparison.OrdinalIgnoreCase));
                Assert.That(hospitality != null
                    && hospitality.affectedByInteractionPromotionWeight);

                DiaryFrequencyMigrationGroupSnapshot external = groups.Find(group =>
                    string.Equals(
                        group?.groupKey,
                        "externalDevTest",
                        StringComparison.OrdinalIgnoreCase));
                Assert.That(external == null);
            }
        }

        /// <summary>Proves the compact Events-tab choice rows survive the real XML loader in order.</summary>
        [Test]
        public static void FrequencyChoicesLoadWithExpectedAbsoluteBands()
        {
            List<DiaryFrequencyChoiceDef> choices = DiaryFrequencyChoices.All();
            string[] tokens = { "rare", "reduced", "normal", "increased" };
            float[] multipliers = { 0.25f, 0.5f, 1f, 2f };
            float[] displayMaximums = { 0.375f, 0.75f, 1.5f, 5f };
            Assert.That(choices.Count == tokens.Length);
            for (int i = 0; i < tokens.Length; i++)
            {
                DiaryFrequencyChoiceDef choice = choices[i];
                Assert.That(choice != null
                    && choice.token == tokens[i]
                    && choice.multiplier == multipliers[i]
                    && choice.displayMaxMultiplier == displayMaximums[i]);
            }
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

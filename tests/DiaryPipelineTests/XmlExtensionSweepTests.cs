using System;
using System.Linq;
using System.Xml.Linq;

namespace DiaryPipelineTests
{
    // Pins the cheap XML extension sweep as data contracts. These checks deliberately exercise the
    // shipped Defs and both translations, so future edits cannot silently return covered events to
    // broad fallback prompts or expose DLC-only settings to players who do not own that content.
    internal static partial class Program
    {
        private static void TestCheapXmlExtensionCoverage()
        {
            XDocument groups = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryInteractionGroupDefs.xml"));
            XDocument important = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryImportantEventDefs.xml"));
            XDocument culture = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryCultureTopicDefs.xml"));
            XDocument windows = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryEventWindowDefs.xml"));
            XDocument observed = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryObservedConditionDefs.xml"));
            XDocument veeObserved = XDocument.Load(
                RepoPath("1.6", "Defs", "Compat", "DiaryObservedConditions_VEE.xml"));
            XDocument eventPrompts = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryEventPromptDefs.xml"));
            XDocument enchantments = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryPromptEnchantmentDefs.xml"));

            XDocument englishGroups = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected",
                "PawnDiary.DiaryInteractionGroupDef", "DiaryInteractionGroupDefs.xml"));
            XDocument russianGroups = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryInteractionGroupDef", "DiaryInteractionGroupDefs.xml"));
            XDocument englishObserved = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected",
                "PawnDiary.DiaryObservedConditionDef", "DiaryObservedConditionDefs.xml"));
            XDocument russianObserved = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryObservedConditionDef", "DiaryObservedConditionDefs.xml"));
            XDocument englishEventPrompts = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected",
                "PawnDiary.DiaryEventPromptDef", "DiaryEventPromptDefs.xml"));
            XDocument russianEventPrompts = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryEventPromptDef", "DiaryEventPromptDefs.xml"));
            XDocument russianImportant = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryImportantEventDef", "DiaryImportantEventDefs.xml"));
            XDocument englishKeyed = XDocument.Load(
                RepoPath("Languages", "English", "Keyed", "PawnDiary.xml"));
            XDocument russianKeyed = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));

            AssertEqual("P0 Word of Inspiration reaches rituals", "ritual",
                ResolveInteractionGroup(groups, "Interaction", "WordOfInspiration", true));
            AssertEqual("P0 engagement reaches romance milestones", "romance_relation",
                ResolveInteractionGroup(groups, "Romance", "Fiance", true));
            AssertEqual("P0 Solar Flare reaches negative mood events", "moodeventNegative",
                ResolveInteractionGroup(groups, "MoodEvent", "SolarFlare", true));

            string[] pollutionDefNames =
            {
                "BiotechPollutionMeaningful", "BiotechPollutionSevere", "BiotechPollutionCritical"
            };
            for (int i = 0; i < pollutionDefNames.Length; i++)
            {
                AssertEqual("P0 pollution group route: " + pollutionDefNames[i],
                    "observedBiotechPollution",
                    ResolveInteractionGroup(groups, "Interaction", pollutionDefNames[i], true));
            }

            XElement pollutionGroup = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "observedBiotechPollution");
            AssertTrue("P0 pollution settings are Biotech-gated",
                HasListValue(pollutionGroup, "enableWhenPackageIdsLoaded",
                    "Ludeon.RimWorld.Biotech"));
            string[] pollutionLocalizedFields = { ".label", ".instruction", ".tone", ".tones.0", ".tones.1" };
            for (int i = 0; i < pollutionLocalizedFields.Length; i++)
            {
                string key = "observedBiotechPollution" + pollutionLocalizedFields[i];
                AssertTrue("P0 English pollution localization exists: " + key,
                    !string.IsNullOrWhiteSpace(KeyedValue(englishGroups, key)));
                AssertTrue("P0 Russian pollution localization exists: " + key,
                    !string.IsNullOrWhiteSpace(KeyedValue(russianGroups, key)));
            }

            XElement engaged = FindDef(
                important, "PawnDiary.DiaryImportantEventDef", "Diary_ImpEvent_Engaged");
            AssertTrue("P0 engagement important-event row exists", engaged != null);
            AssertEqual("P0 engagement event kind", "relation.fiance.gained",
                ChildValue(engaged, "eventKind"));
            AssertTrue("P0 engagement row matches Fiance",
                HasListValue(engaged, "matchDefNames", "Fiance"));
            AssertTrue("P0 Russian engagement label exists",
                !string.IsNullOrWhiteSpace(
                    KeyedValue(russianImportant, "Diary_ImpEvent_Engaged.label")));
            AssertTrue("P0 Russian engagement template keeps the other pawn slot",
                (KeyedValue(russianImportant, "Diary_ImpEvent_Engaged.lineTemplate") ?? string.Empty)
                    .Contains("{other}"));

            AssertCultureTriggers(culture, "Diary_CultureTopic_Space",
                new[] { "LaunchedShip", "EnteredCryptosleep", "PutIntoCryptosleep" });
            AssertCultureTriggers(culture, "Diary_CultureTopic_Disasters",
                new[] { "Blight", "UnnaturalDarkness", "VolcanicAsh" });
            AssertCultureTriggers(culture, "Diary_CultureTopic_Void",
                new[] { "Gleaming" });

            XElement mechCluster = FindDef(
                windows, "PawnDiary.DiaryEventWindowDef", "MechClusterLanded");
            AssertTrue("P0 MechCluster window is Royalty-gated",
                HasListValue(mechCluster, "enableWhenPackageIdsLoaded",
                    "Ludeon.RimWorld.Royalty"));

            string[,] observerRows =
            {
                { "EclipseActive", "Eclipse", "" },
                { "PsychicDroneActive", "PsychicDrone", "" },
                { "PsychicSootheActive", "PsychicSoothe", "" },
                { "PsychicSuppressionActive", "PsychicSuppression", "Ludeon.RimWorld.Royalty" },
                { "GrayPallActive", "GrayPall", "Ludeon.RimWorld.Anomaly" },
                { "UnnaturalHeatActive", "UnnaturalHeat", "Ludeon.RimWorld.Anomaly" },
                { "HateChantDroneActive", "HateChantDrone", "Ludeon.RimWorld.Anomaly" },
                { "NoxiousHazeActive", "NoxiousHaze", "Ludeon.RimWorld.Biotech" },
                { "DroughtActive", "Drought", "Ludeon.RimWorld.Odyssey" },
                { "VolcanicDebrisActive", "VolcanicDebris", "Ludeon.RimWorld.Odyssey" },
                { "LavaFlowActive", "LavaFlow", "Ludeon.RimWorld.Odyssey" },
                { "DarkenedSkiesActive", "DarkenedSkies", "Ludeon.RimWorld.Odyssey" },
                { "BioluminescentSporesActive", "BioluminescentSpores", "Ludeon.RimWorld.Odyssey" },
                { "GillRotActive", "GillRot", "Ludeon.RimWorld.Odyssey" },
                { "DeepFreezeActive", "DeepFreeze", "Ludeon.RimWorld.Odyssey" }
            };
            for (int i = 0; i < observerRows.GetLength(0); i++)
            {
                string defName = observerRows[i, 0];
                XElement def = FindDef(
                    observed, "PawnDiary.DiaryObservedConditionDef", defName);
                AssertTrue("P1 observer exists: " + defName, def != null);
                AssertEqual("P1 observer uses live GameCondition state: " + defName,
                    "GameCondition", ChildValue(def, "observerType"));
                AssertTrue("P1 observer matches exact vanilla defName: " + defName,
                    HasListValue(def, "matchDefNames", observerRows[i, 1]));
                AssertEqual("P1 observer does not synthesize a start page: " + defName,
                    "false", ChildValue(def, "recordStartEvent"));
                AssertEqual("P1 observer does not synthesize an end page: " + defName,
                    "false", ChildValue(def, "recordEndEvent"));

                string packageId = observerRows[i, 2];
                if (string.IsNullOrWhiteSpace(packageId))
                {
                    AssertTrue("P1 base observer has no DLC requirement: " + defName,
                        def.Attribute("MayRequire") == null);
                }
                else
                {
                    AssertTrue("P1 DLC observer carries MayRequire: " + defName,
                        XmlRowHasMayRequire(def, packageId));
                }

                AssertTrue("P1 English observer label exists: " + defName,
                    !string.IsNullOrWhiteSpace(KeyedValue(englishObserved, defName + ".label")));
                AssertTrue("P1 Russian observer label exists: " + defName,
                    !string.IsNullOrWhiteSpace(KeyedValue(russianObserved, defName + ".label")));
                string[] promptFields =
                {
                    ChildValue(def, "promptPriorityKey"),
                    ChildValue(def, "promptConditionKey"),
                    ChildValue(def, "promptDescriptionKey")
                };
                for (int field = 0; field < promptFields.Length; field++)
                {
                    AssertTrue("P1 English observer prompt text exists: " + promptFields[field],
                        !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, promptFields[field])));
                    AssertTrue("P1 Russian observer prompt text exists: " + promptFields[field],
                        !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, promptFields[field])));
                }
            }

            string[,] moodRoutes =
            {
                { "PsychicSoothe", "moodeventPositive" },
                { "BioluminescentSpores", "moodeventPositive" },
                { "UnnaturalHeat", "moodeventWeatherHardship" },
                { "Drought", "moodeventWeatherHardship" },
                { "DarkenedSkies", "moodeventWeatherHardship" },
                { "DeepFreeze", "moodeventWeatherHardship" },
                { "VolcanicDebris", "moodeventStormDanger" },
                { "LavaFlow", "moodeventStormDanger" },
                { "Eclipse", "moodeventNegative" },
                { "PsychicDrone", "moodeventNegative" },
                { "GrayPall", "moodeventNegative" },
                { "NoxiousHaze", "moodeventNegative" },
                { "HateChantDrone", "moodeventNegative" },
                { "GillRot", "moodeventNegative" },
                { "PsychicSuppression", "moodeventMixed" }
            };
            for (int i = 0; i < moodRoutes.GetLength(0); i++)
            {
                AssertEqual("P1 observed-condition start-page route: " + moodRoutes[i, 0],
                    moodRoutes[i, 1],
                    ResolveInteractionGroup(groups, "MoodEvent", moodRoutes[i, 0], true));
            }

            XElement[] veeRows = veeObserved
                .Descendants("PawnDiary.DiaryObservedConditionDef").ToArray();
            AssertEqual("P0 six VEE observer settings rows remain", 6, veeRows.Length);
            for (int i = 0; i < veeRows.Length; i++)
            {
                AssertTrue("P0 VEE observer is package-gated: " + ChildValue(veeRows[i], "defName"),
                    XmlRowHasMayRequire(veeRows[i], "VanillaExpanded.VEE"));
            }

            string[] rotationGroups = { "heartfelt", "smalltalk", "socialfight", "talecombat" };
            for (int i = 0; i < rotationGroups.Length; i++)
            {
                XElement group = FindDef(
                    groups, "PawnDiary.DiaryInteractionGroupDef", rotationGroups[i]);
                AssertEqual("P1 instruction pool has four rows: " + rotationGroups[i], 4,
                    group?.Element("instructions")?.Elements("li").Count() ?? 0);
                string key = rotationGroups[i] + ".instructions.3";
                AssertTrue("P1 English fourth instruction exists: " + key,
                    !string.IsNullOrWhiteSpace(KeyedValue(englishGroups, key)));
                AssertTrue("P1 Russian fourth instruction exists: " + key,
                    !string.IsNullOrWhiteSpace(KeyedValue(russianGroups, key)));
            }

            string[,] promptRows =
            {
                { "DiaryEventPrompt_AnomalyStudyBreakthrough", "anomalyStudyBreakthrough", "Ludeon.RimWorld.Anomaly" },
                { "DiaryEventPrompt_AnomalyContainmentBreach", "anomalyContainmentBreach", "Ludeon.RimWorld.Anomaly" },
                { "DiaryEventPrompt_AnomalyCreepJoinerOutcome", "anomalyCreepJoinerOutcome", "Ludeon.RimWorld.Anomaly" },
                { "DiaryEventPrompt_AnomalyGhoulTransformation", "anomalyGhoulTransformation", "Ludeon.RimWorld.Anomaly" },
                { "DiaryEventPrompt_AnomalyVoidOutcome", "anomalyVoidOutcome", "Ludeon.RimWorld.Anomaly" },
                { "DiaryEventPrompt_RitualAnomalyInvitation", "ritualAnomalyInvitation", "Ludeon.RimWorld.Anomaly" },
                { "DiaryEventPrompt_RitualAnomalyFleshAndWeather", "ritualAnomalyFleshAndWeather", "Ludeon.RimWorld.Anomaly" },
                { "DiaryEventPrompt_RitualAnomalyPredation", "ritualAnomalyPredation", "Ludeon.RimWorld.Anomaly" },
                { "DiaryEventPrompt_RitualAnomalyMind", "ritualAnomalyMind", "Ludeon.RimWorld.Anomaly" },
                { "DiaryEventPrompt_RitualAnomalyAbduction", "ritualAnomalyAbduction", "Ludeon.RimWorld.Anomaly" },
                { "DiaryEventPrompt_RitualAnomalyDeathRefusal", "ritualAnomalyDeathRefusal", "Ludeon.RimWorld.Anomaly" },
                { "DiaryEventPrompt_RitualAnomalyPsychic", "ritualAnomalyPsychic", "Ludeon.RimWorld.Anomaly" },
                { "DiaryEventPrompt_BiotechFamilyBirth", "biotechFamilyBirth", "Ludeon.RimWorld.Biotech" },
                { "DiaryEventPrompt_BiotechDeathrestInterrupted", "biotechDeathrestInterrupted", "Ludeon.RimWorld.Biotech" },
                { "DiaryEventPrompt_OdysseyGravshipLanding", "odysseyGravshipLanding", "Ludeon.RimWorld.Odyssey" },
                { "DiaryEventPrompt_RecruitGroup", "recruit", "" },
                { "DiaryEventPrompt_TrialGroup", "trial", "Ludeon.RimWorld.Ideology" },
                { "DiaryEventPrompt_ConversionGroup", "conversion", "Ludeon.RimWorld.Ideology" },
                { "DiaryEventPrompt_SlaveryGroup", "slavery", "Ludeon.RimWorld.Ideology" }
            };
            for (int i = 0; i < promptRows.GetLength(0); i++)
            {
                string defName = promptRows[i, 0];
                XElement def = FindDef(
                    eventPrompts, "PawnDiary.DiaryEventPromptDef", defName);
                AssertTrue("P1 group event prompt exists: " + defName, def != null);
                AssertEqual("P1 group event prompt key: " + defName,
                    promptRows[i, 1], ChildValue(def, "eventType"));
                if (!string.IsNullOrWhiteSpace(promptRows[i, 2]))
                {
                    AssertTrue("P1 group event prompt is DLC-gated: " + defName,
                        HasListValue(def, "enableWhenPackageIdsLoaded", promptRows[i, 2]));
                }

                string[] localizedFields = { ".label", ".prompt", ".enhancement" };
                for (int field = 0; field < localizedFields.Length; field++)
                {
                    string key = defName + localizedFields[field];
                    AssertTrue("P1 English group prompt localization exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(englishEventPrompts, key)));
                    AssertTrue("P1 Russian group prompt localization exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(russianEventPrompts, key)));
                }
            }

            string[] royaltyPromptTypes =
            {
                "PersonaWeapon", "PersonaWeaponBondFormed", "PersonaWeaponBondSeparated",
                "PersonaWeaponBondRecovered", "PersonaWeaponBondEnded",
                "PersonaWeaponFirstConsequentialKill", "RoyalTitleGained", "RoyalTitlePromoted",
                "RoyalTitleDemoted", "RoyalTitleLost", "RoyalSuccession", "RoyalHeirAppointed",
                "PsylinkLevel", "BestowingCeremony", "AnimaTreeLinking"
            };
            for (int i = 0; i < royaltyPromptTypes.Length; i++)
            {
                XElement def = eventPrompts
                    .Descendants("PawnDiary.DiaryEventPromptDef")
                    .SingleOrDefault(row => string.Equals(
                        ChildValue(row, "eventType"), royaltyPromptTypes[i],
                        StringComparison.OrdinalIgnoreCase));
                AssertTrue("P0 Royalty prompt settings are gated: " + royaltyPromptTypes[i],
                    HasListValue(def, "enableWhenPackageIdsLoaded",
                        "Ludeon.RimWorld.Royalty"));
            }

            string[,] enchantmentRows =
            {
                { "DiaryEnchant_Blindness", "PawnDiary.Prompt.Health.Description.Blindness", "PawnDiary.Prompt.Health.Cue.WorldWithoutSight" },
                { "DiaryEnchant_TraumaSavant", "PawnDiary.Prompt.Health.Description.TraumaSavant", "PawnDiary.Prompt.Health.Cue.ThoughtWithoutOrdinaryFeeling" },
                { "DiaryEnchant_Joywire", "PawnDiary.Prompt.Health.Description.Joywire", "PawnDiary.Prompt.Health.Cue.ArtificialCheer" },
                { "DiaryEnchant_Pregnancy", "PawnDiary.Prompt.Health.Description.Pregnancy", "PawnDiary.Prompt.Health.Cue.BodyCarryingNewLife" },
                { "DiaryEnchant_VoidShockOrTouched", "PawnDiary.Prompt.Health.Description.VoidShockOrTouched", "PawnDiary.Prompt.Health.Cue.VoidAfterimage" },
                { "DiaryEnchant_Carcinoma", "PawnDiary.Prompt.Health.Description.Carcinoma", "PawnDiary.Prompt.Health.Cue.SlowInternalThreat" },
                { "DiaryEnchant_Deathrest", "PawnDiary.Prompt.Health.Description.Deathrest", "PawnDiary.Prompt.Health.Cue.DeathlikeSleep" },
                { "DiaryEnchant_LungRot", "PawnDiary.Prompt.Health.Description.LungRot", "PawnDiary.Prompt.Health.Cue.BreathUnderStrain" },
                { "DiaryEnchant_VacuumExposure", "PawnDiary.Prompt.Health.Description.VacuumExposure", "PawnDiary.Prompt.Health.Cue.VacuumBurning" },
                { "DiaryEnchant_GravNausea", "PawnDiary.Prompt.Health.Description.GravNausea", "PawnDiary.Prompt.Health.Cue.GravityTurningStomach" }
            };
            for (int i = 0; i < enchantmentRows.GetLength(0); i++)
            {
                string defName = enchantmentRows[i, 0];
                XElement def = FindDef(
                    enchantments, "PawnDiary.DiaryPromptEnchantmentDef", defName);
                AssertEqual("P1 enchantment owns a keyed description: " + defName,
                    enchantmentRows[i, 1], ChildValue(def, "descriptionOverrideKey"));
                AssertTrue("P1 enchantment owns a cue: " + defName,
                    HasListValue(def, "cueKeys", enchantmentRows[i, 2]));
                for (int field = 1; field <= 2; field++)
                {
                    string key = enchantmentRows[i, field];
                    AssertTrue("P1 English enchantment text exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, key)));
                    AssertTrue("P1 Russian enchantment text exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, key)));
                }
            }
        }

        private static void AssertCultureTriggers(
            XDocument culture,
            string defName,
            string[] expectedDefNames)
        {
            XElement topic = FindDef(culture, "PawnDiary.DiaryCultureTopicDef", defName);
            AssertTrue("P0 culture topic exists: " + defName, topic != null);
            for (int i = 0; i < expectedDefNames.Length; i++)
            {
                AssertTrue("P0 culture trigger exists: " + defName + "/" + expectedDefNames[i],
                    HasListValue(topic, "triggerDefNames", expectedDefNames[i]));
            }
        }

        private static bool XmlRowHasMayRequire(XElement def, string packageId)
        {
            return string.Equals(
                (string)def?.Attribute("MayRequire"),
                packageId,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}

// Focused pure and XML-contract tests for the compact Events-tab frequency choices. Numeric values,
// menu order, and inherited-value display bands are all XML-owned; these tests prevent UI code from
// growing an independent hardcoded policy.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestDiaryFrequencyChoicePolicy()
        {
            TestFrequencyChoiceNormalizationAndBands();
            TestFrequencyChoiceXmlAndLocalizationContract();
        }

        private static void TestFrequencyChoiceNormalizationAndBands()
        {
            List<DiaryFrequencyChoiceSnapshot> raw = new List<DiaryFrequencyChoiceSnapshot>
            {
                Choice("increased", 2f, 5f, 40),
                Choice("normal", 1f, 1.5f, 30),
                Choice("rare", 0.25f, 0.375f, 10),
                Choice("reduced", 0.5f, 0.75f, 20),
                Choice(" RARE ", 0.9f, 1f, 99),
                Choice(string.Empty, 1f, 1f, 0),
                Choice("invalid", float.NaN, 2f, 50)
            };

            List<DiaryFrequencyChoiceSnapshot> normalized =
                DiaryFrequencyChoicePolicy.NormalizeForMenu(raw);
            AssertEqual("choice policy keeps four unique valid rows", 4, normalized.Count);
            AssertEqual("choice policy orders rare first", "rare", normalized[0].choiceKey);
            AssertEqual("choice policy orders reduced second", "reduced", normalized[1].choiceKey);
            AssertEqual("choice policy orders normal third", "normal", normalized[2].choiceKey);
            AssertEqual("choice policy orders increased fourth", "increased", normalized[3].choiceKey);
            AssertNear("first duplicate choice wins", 0.25f, normalized[0].multiplier);

            AssertEqual("0.15 inherited value displays Rare", "rare",
                DiaryFrequencyChoicePolicy.ChoiceForMultiplier(raw, 0.15f).choiceKey);
            AssertEqual("0.375 inclusive boundary displays Rare", "rare",
                DiaryFrequencyChoicePolicy.ChoiceForMultiplier(raw, 0.375f).choiceKey);
            AssertEqual("Lite significant 0.6 displays Reduced", "reduced",
                DiaryFrequencyChoicePolicy.ChoiceForMultiplier(raw, 0.6f).choiceKey);
            AssertEqual("Standard 1x displays Normal", "normal",
                DiaryFrequencyChoicePolicy.ChoiceForMultiplier(raw, 1f).choiceKey);
            AssertEqual("Frequent significant 1.35x displays Normal", "normal",
                DiaryFrequencyChoicePolicy.ChoiceForMultiplier(raw, 1.35f).choiceKey);
            AssertEqual("Frequent routine 1.75x displays Increased", "increased",
                DiaryFrequencyChoicePolicy.ChoiceForMultiplier(raw, 1.75f).choiceKey);
            AssertEqual("defensive cap displays Increased", "increased",
                DiaryFrequencyChoicePolicy.ChoiceForMultiplier(raw, 5f).choiceKey);
            AssertEqual("NaN display safely maps to Normal", "normal",
                DiaryFrequencyChoicePolicy.ChoiceForMultiplier(raw, float.NaN).choiceKey);
            AssertTrue("empty choice list has no display choice",
                DiaryFrequencyChoicePolicy.ChoiceForMultiplier(null, 1f) == null);
        }

        private static void TestFrequencyChoiceXmlAndLocalizationContract()
        {
            XDocument defs = XDocument.Load(RepoPath(
                "1.6", "Defs", "DiaryFrequencyChoiceDefs.xml"));
            XDocument english = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected",
                "PawnDiary.DiaryFrequencyChoiceDef", "DiaryFrequencyChoiceDefs.xml"));
            XDocument russian = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryFrequencyChoiceDef", "DiaryFrequencyChoiceDefs.xml"));
            XDocument englishKeyed = XDocument.Load(RepoPath(
                "Languages", "English", "Keyed", "PawnDiary.xml"));
            XDocument russianKeyed = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            XDocument uiStyle = XDocument.Load(RepoPath(
                "1.6", "Defs", "DiaryUiStyleDef.xml"));

            XElement[] rows = defs.Root.Elements("PawnDiary.DiaryFrequencyChoiceDef").ToArray();
            AssertEqual("exactly four shipped frequency choices", 4, rows.Length);

            string[] expectedTokens = { "rare", "reduced", "normal", "increased" };
            float[] expectedMultipliers = { 0.25f, 0.5f, 1f, 2f };
            float previousBand = -1f;
            int previousOrder = int.MinValue;
            for (int i = 0; i < rows.Length; i++)
            {
                XElement row = rows[i];
                string defName = ChildValue(row, "defName");
                string token = ChildValue(row, "token");
                float multiplier = float.Parse(
                    ChildValue(row, "multiplier"), CultureInfo.InvariantCulture);
                float band = float.Parse(
                    ChildValue(row, "displayMaxMultiplier"), CultureInfo.InvariantCulture);
                int order = int.Parse(ChildValue(row, "order"), CultureInfo.InvariantCulture);

                AssertEqual("frequency choice token " + i, expectedTokens[i], token);
                AssertNear("frequency choice multiplier " + token, expectedMultipliers[i], multiplier);
                AssertTrue("frequency display bands strictly increase: " + token, band > previousBand);
                AssertTrue("frequency choice orders strictly increase: " + token, order > previousOrder);
                AssertEqual("English choice label mirrors base XML: " + token,
                    ChildValue(row, "label"), KeyedValue(english, defName + ".label"));
                AssertEqual("English choice description mirrors base XML: " + token,
                    ChildValue(row, "description"), KeyedValue(english, defName + ".description"));
                AssertTrue("Russian choice label exists: " + token,
                    !string.IsNullOrWhiteSpace(KeyedValue(russian, defName + ".label")));
                AssertTrue("Russian choice description exists: " + token,
                    !string.IsNullOrWhiteSpace(KeyedValue(russian, defName + ".description")));

                previousBand = band;
                previousOrder = order;
            }

            AssertNear("highest choice display band reaches defensive cap",
                DiaryFrequencyPolicy.MaximumMultiplier, previousBand);

            XElement styleRow = uiStyle.Root.Element("PawnDiary.DiaryUiStyleDef");
            string[] eventMetricNames =
            {
                "settingsEventsCompactHeightThreshold",
                "settingsEventsTitleHeight",
                "settingsEventsPresetHeaderHeight",
                "settingsEventsPresetCardHeight",
                "settingsEventsCompactPresetCardHeight",
                "settingsEventsPresetStatusHeight",
                "settingsEventsEnableAllButtonWidth",
                "settingsEventsResetButtonWidth",
                "settingsEventsDismissButtonWidth",
                "settingsEventsFrequencyButtonWidth",
                "settingsEventsDomainHeight",
                "settingsEventsRowHeight",
                "settingsEventsRowGap"
            };
            float[] expectedEventMetrics =
            {
                500f, 30f, 30f, 84f, 38f, 26f, 118f,
                142f, 100f, 132f, 28f, 32f, 3f
            };
            for (int i = 0; i < eventMetricNames.Length; i++)
            {
                string raw = ChildValue(styleRow, eventMetricNames[i]);
                float value;
                AssertTrue("Events UI style metric parses: " + eventMetricNames[i],
                    float.TryParse(
                        raw,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value));
                AssertNear("Events UI style metric is pinned: " + eventMetricNames[i],
                    expectedEventMetrics[i], value);
            }

            string[] uiKeys =
            {
                "PawnDiary.Settings.EventFilters.FrequencyTitle",
                "PawnDiary.Settings.EventFilters.ResetToPreset",
                "PawnDiary.Settings.EventFilters.ResetToPresetConfirm",
                "PawnDiary.Settings.EventFilters.SelectPresetConfirm",
                "PawnDiary.Settings.EventFilters.PresetStatusInherited",
                "PawnDiary.Settings.EventFilters.PresetStatusCustom",
                "PawnDiary.Settings.EventFilters.PresetStatusMigrated",
                "PawnDiary.Settings.EventFilters.MigrationNotice",
                "PawnDiary.Settings.EventFilters.MigrationNoticeCompact",
                "PawnDiary.Settings.EventFilters.Search",
                "PawnDiary.Settings.EventFilters.NoSearchResults",
                "PawnDiary.Settings.EventFilters.DomainHeading",
                "PawnDiary.Settings.EventFilters.DomainHeadingSearch",
                "PawnDiary.Settings.EventFilters.SearchDomainTip",
                "PawnDiary.Settings.EventFilters.FrequencyTipCustom",
                "PawnDiary.Settings.EventFilters.FrequencyTipInherited"
            };
            for (int i = 0; i < uiKeys.Length; i++)
            {
                AssertTrue("English frequency UI key exists: " + uiKeys[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, uiKeys[i])));
                AssertTrue("Russian frequency UI key exists: " + uiKeys[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, uiKeys[i])));
            }

            string[] domains =
            {
                "Interaction", "MentalState", "Tale", "MoodEvent", "Thought",
                "Inspiration", "Romance", "Work", "Hediff", "Raid", "Quest", "Ritual",
                "Ability", "Progression", "Reflection", "GravshipJourney", "PersonaWeapon",
                "RoyalPermit"
            };
            for (int i = 0; i < domains.Length; i++)
            {
                string key = "PawnDiary.Settings.EventFilters.Domain." + domains[i];
                AssertTrue("English frequency domain key exists: " + domains[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, key)));
                AssertTrue("Russian frequency domain key exists: " + domains[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, key)));
            }
        }

        private static DiaryFrequencyChoiceSnapshot Choice(
            string key,
            float multiplier,
            float displayMax,
            int order)
        {
            return new DiaryFrequencyChoiceSnapshot
            {
                choiceKey = key,
                multiplier = multiplier,
                displayMaxMultiplier = displayMax,
                order = order
            };
        }
    }
}

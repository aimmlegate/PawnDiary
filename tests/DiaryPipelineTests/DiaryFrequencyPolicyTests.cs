// Focused pure coverage for the frequency foundation. These tests pin the existing native Work,
// Ability, and interaction-promotion baselines, then exercise the new detached resolver against all
// shipped core and compatibility group XML. Runtime capture is deliberately unchanged in Slice 1.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestDiaryFrequencyPolicy()
        {
            TestDiaryFrequencyTierTokens();
            TestDiaryFrequencyResolution();
            TestDiaryFrequencyAdmission();
            TestDiaryFrequencyCustomDetection();
            TestDiaryFrequencyXmlContract();
            TestDiaryFrequencyLegacyNativeBaselines();
            TestDiaryFrequencySyntheticAdmissionVolumeFixture();
        }

        private static void TestDiaryFrequencyTierTokens()
        {
            AssertEqual("essential tier normalizes", DiaryFrequencyTiers.Essential,
                DiaryFrequencyTiers.Normalize(" Essential "));
            AssertEqual("significant tier normalizes", DiaryFrequencyTiers.Significant,
                DiaryFrequencyTiers.Normalize("SIGNIFICANT"));
            AssertEqual("routine tier normalizes", DiaryFrequencyTiers.Routine,
                DiaryFrequencyTiers.Normalize("routine"));
            AssertEqual("ambient tier normalizes", DiaryFrequencyTiers.Ambient,
                DiaryFrequencyTiers.Normalize("ambient"));
            AssertEqual("unknown tier stays detectable", string.Empty,
                DiaryFrequencyTiers.Normalize("important-ish"));
            AssertTrue("blank tier is unknown", !DiaryFrequencyTiers.IsKnown("  "));
        }

        private static void TestDiaryFrequencyResolution()
        {
            DiaryFrequencyPresetSnapshot preset = new DiaryFrequencyPresetSnapshot
            {
                presetKey = "fixture"
            };
            preset.tierMultipliers[DiaryFrequencyTiers.Essential] = 1f;
            preset.tierMultipliers[DiaryFrequencyTiers.Routine] = 0.3f;
            preset.groupOverrides["dayreflection"] = 0.45f;

            AssertNear("exact group override wins over tier", 0.45f,
                DiaryFrequencyPolicy.ResolvePresetMultiplier(
                    preset, "DAYREFLECTION", DiaryFrequencyTiers.Routine));
            AssertNear("known tier inherits preset", 0.3f,
                DiaryFrequencyPolicy.ResolvePresetMultiplier(
                    preset, "workRoutine", DiaryFrequencyTiers.Routine));
            AssertNear("missing tier multiplier safely falls back to Standard", 1f,
                DiaryFrequencyPolicy.ResolvePresetMultiplier(
                    preset, "romance", DiaryFrequencyTiers.Significant));
            AssertNear("unknown third-party tier safely falls back to Standard", 1f,
                DiaryFrequencyPolicy.ResolvePresetMultiplier(
                    preset, "thirdParty", "cinematic"));
            AssertNear("exact override can intentionally cover a future tier", 0.45f,
                DiaryFrequencyPolicy.ResolvePresetMultiplier(
                    preset, "dayreflection", "future-tier"));
            AssertNear("missing preset safely falls back to Standard", 1f,
                DiaryFrequencyPolicy.ResolvePresetMultiplier(
                    null, "workRoutine", DiaryFrequencyTiers.Ambient));

            preset.tierMultipliers[DiaryFrequencyTiers.Significant] = -2f;
            AssertNear("negative preset multiplier clamps to zero", 0f,
                DiaryFrequencyPolicy.ResolvePresetMultiplier(
                    preset, "romance", DiaryFrequencyTiers.Significant));
            preset.tierMultipliers[DiaryFrequencyTiers.Significant] = 12f;
            AssertNear("oversized preset multiplier clamps to defensive cap", 5f,
                DiaryFrequencyPolicy.ResolvePresetMultiplier(
                    preset, "romance", DiaryFrequencyTiers.Significant));
            preset.tierMultipliers[DiaryFrequencyTiers.Significant] = float.NaN;
            AssertNear("NaN preset multiplier falls back to Standard", 1f,
                DiaryFrequencyPolicy.ResolvePresetMultiplier(
                    preset, "romance", DiaryFrequencyTiers.Significant));
            preset.tierMultipliers[DiaryFrequencyTiers.Significant] = float.PositiveInfinity;
            AssertNear("infinite preset multiplier falls back to Standard", 1f,
                DiaryFrequencyPolicy.ResolvePresetMultiplier(
                    preset, "romance", DiaryFrequencyTiers.Significant));

            AssertNear("valid player override wins", 2.25f,
                DiaryFrequencyPolicy.ResolveEffectiveMultiplier(
                    preset, "workRoutine", DiaryFrequencyTiers.Routine, true, 2.25f));
            AssertNear("invalid player override inherits preset", 0.3f,
                DiaryFrequencyPolicy.ResolveEffectiveMultiplier(
                    preset, "workRoutine", DiaryFrequencyTiers.Routine, true, float.NaN));
            AssertNear("negative player override clamps to zero", 0f,
                DiaryFrequencyPolicy.ResolveEffectiveMultiplier(
                    preset, "workRoutine", DiaryFrequencyTiers.Routine, true, -1f));
            AssertNear("oversized player override clamps to cap", 5f,
                DiaryFrequencyPolicy.ResolveEffectiveMultiplier(
                    preset, "workRoutine", DiaryFrequencyTiers.Routine, true, 99f));
        }

        private static void TestDiaryFrequencyAdmission()
        {
            DiaryFrequencyPresetSnapshot preset = new DiaryFrequencyPresetSnapshot();
            preset.tierMultipliers[DiaryFrequencyTiers.Routine] = 0.5f;

            DiaryFrequencyRequest request = new DiaryFrequencyRequest
            {
                groupKey = "workPassion",
                frequencyTier = DiaryFrequencyTiers.Routine,
                nativeCaptureChance = 0.6f,
                preset = preset,
                enabled = true,
                roll = 0.3f
            };

            DiaryFrequencyDecision decision = DiaryFrequencyPolicy.Decide(request);
            AssertTrue("roll equal to effective chance is accepted", decision.Accepted);
            AssertTrue("accepted decision is typed", decision.reason == DiaryFrequencyDecisionReason.Accepted);
            AssertNear("decision reports resolved multiplier", 0.5f, decision.multiplier);
            AssertNear("decision reports multiplied chance", 0.3f, decision.effectiveChance);

            request.strictRollBoundary = true;
            decision = DiaryFrequencyPolicy.Decide(request);
            AssertTrue("strict upstream boundary rejects an equal roll", !decision.Accepted);
            request.strictRollBoundary = false;

            request.roll = 0.3001f;
            decision = DiaryFrequencyPolicy.Decide(request);
            AssertTrue("roll above effective chance is rejected", !decision.Accepted);
            AssertTrue("frequency rejection is distinguishable",
                decision.reason == DiaryFrequencyDecisionReason.RejectedByFrequency);

            request.enabled = false;
            request.bypassFrequency = true;
            request.nativeCaptureChance = float.NaN;
            request.roll = float.NaN;
            decision = DiaryFrequencyPolicy.Decide(request);
            AssertTrue("hard disable wins over bypass", decision.reason == DiaryFrequencyDecisionReason.Disabled);

            request.enabled = true;
            decision = DiaryFrequencyPolicy.Decide(request);
            AssertTrue("forced/manual bypass does not need a chance draw", decision.Accepted);
            AssertTrue("bypass reason is explicit",
                decision.reason == DiaryFrequencyDecisionReason.AcceptedBypass);

            request.bypassFrequency = false;
            decision = DiaryFrequencyPolicy.Decide(request);
            AssertTrue("non-finite admission inputs fail safely",
                decision.reason == DiaryFrequencyDecisionReason.Invalid);

            request.nativeCaptureChance = 4f;
            request.roll = 1f;
            request.hasPlayerOverride = true;
            request.playerOverride = 4f;
            decision = DiaryFrequencyPolicy.Decide(request);
            AssertTrue("effective chance clamps at one", decision.Accepted);
            AssertNear("effective chance cap is reported", 1f, decision.effectiveChance);

            request.nativeCaptureChance = 2f;
            request.roll = 0.6f;
            request.playerOverride = 0.3f;
            decision = DiaryFrequencyPolicy.Decide(request);
            AssertTrue("native chance is multiplied before the final clamp", decision.Accepted);
            AssertNear("above-one native chance can be reduced by a preset", 0.6f,
                decision.effectiveChance);

            request.nativeCaptureChance = 0f;
            request.roll = 0f;
            request.playerOverride = 0f;
            decision = DiaryFrequencyPolicy.Decide(request);
            AssertTrue("zero probability is closed even at the zero roll boundary", !decision.Accepted);
            AssertTrue("zero probability reports frequency rejection",
                decision.reason == DiaryFrequencyDecisionReason.RejectedByFrequency);

            request.groupKey = " ";
            decision = DiaryFrequencyPolicy.Decide(request);
            AssertTrue("blank group identity is invalid",
                decision.reason == DiaryFrequencyDecisionReason.Invalid);
            AssertTrue("null request is invalid",
                DiaryFrequencyPolicy.Decide(null).reason == DiaryFrequencyDecisionReason.Invalid);
        }

        private static void TestDiaryFrequencyCustomDetection()
        {
            DiaryFrequencyPresetSnapshot preset = new DiaryFrequencyPresetSnapshot();
            preset.tierMultipliers[DiaryFrequencyTiers.Routine] = 0.3f;
            List<DiaryFrequencyGroupSnapshot> groups = new List<DiaryFrequencyGroupSnapshot>
            {
                new DiaryFrequencyGroupSnapshot
                {
                    groupKey = "workPassion",
                    frequencyTier = DiaryFrequencyTiers.Routine
                }
            };

            Dictionary<string, float> overrides = new Dictionary<string, float>(StringComparer.Ordinal)
            {
                ["WORKPASSION"] = 0.3f
            };
            AssertTrue("override equal to inherited preset is not Custom",
                !DiaryFrequencyPolicy.HasCustomOverrides(overrides, groups, preset));

            overrides["WORKPASSION"] = 0.6f;
            AssertTrue("different known-row override makes preset Custom",
                DiaryFrequencyPolicy.HasCustomOverrides(overrides, groups, preset));

            overrides.Clear();
            overrides["futureModGroup"] = 0.1f;
            AssertTrue("unknown future override is preserved but not shown as a known Custom row",
                !DiaryFrequencyPolicy.HasCustomOverrides(overrides, groups, preset));

            overrides.Clear();
            overrides["workPassion"] = float.NaN;
            AssertTrue("corrupt override does not create a false Custom state",
                !DiaryFrequencyPolicy.HasCustomOverrides(overrides, groups, preset));
        }

        private static void TestDiaryFrequencyXmlContract()
        {
            List<XElement> groups = AllShippedFrequencyGroupDefs();
            XDocument coreDocument = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryInteractionGroupDefs.xml"));
            XElement[] coreGroups = coreDocument
                .Descendants("PawnDiary.DiaryInteractionGroupDef").ToArray();

            AssertEqual("core interaction-group baseline remains 120 rows", 120, coreGroups.Length);
            AssertEqual("core plus compatibility catalog has 148 rows", 148, groups.Count);
            AssertEqual("every shipped group key is unique", groups.Count,
                groups.Select(group => ChildValue(group, "defName"))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count());

            Dictionary<string, XElement> groupByKey = groups.ToDictionary(
                group => ChildValue(group, "defName"),
                group => group,
                StringComparer.OrdinalIgnoreCase);
            XElement[] externalGroups = groups
                .Where(group => string.Equals(
                    ChildValue(group, "domain"), "External", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual("only the dev integration row is External", 1, externalGroups.Length);
            AssertEqual("External row stays outside player-frequency policy", string.Empty,
                ChildValue(externalGroups[0], "frequencyTier"));

            Dictionary<string, int> tierCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            XElement[] playerGroups = groups.Except(externalGroups).ToArray();
            AssertEqual("all 147 non-External rows are frequency-controlled", 147, playerGroups.Length);
            foreach (XElement group in playerGroups)
            {
                string key = ChildValue(group, "defName");
                string rawTier = ChildValue(group, "frequencyTier");
                string tier = DiaryFrequencyTiers.Normalize(rawTier);
                AssertTrue("group has a known explicit frequency tier: " + key, tier.Length > 0);
                int count;
                tierCounts.TryGetValue(tier, out count);
                tierCounts[tier] = count + 1;
            }

            string[] supportedTiers =
            {
                DiaryFrequencyTiers.Essential,
                DiaryFrequencyTiers.Significant,
                DiaryFrequencyTiers.Routine,
                DiaryFrequencyTiers.Ambient
            };
            foreach (string tier in supportedTiers)
            {
                AssertTrue("shipped catalog uses frequency tier: " + tier,
                    tierCounts.ContainsKey(tier) && tierCounts[tier] > 0);
            }

            // These are semantic promises, not merely a count of whichever rows happen to say
            // "essential" today. A future edit that thins a protected milestone must fail here.
            string[] protectedGroupKeys =
            {
                "talecombat",              // Canonical death fallback and lethal Tales.
                "biotechFamilyBirth",      // Exact post-outcome Biotech birth.
                "talelife",                // Vanilla birth and recruitment milestones.
                "ritualChildbirth",
                "arrival",                 // Canonical faction-change recruitment/arrival.
                "raid",                    // Generic hostile raid fallback.
                "raidDropPod",
                "raidInfestation",
                "raidAnomalyEntities",
                "eventWindowMechCluster",
                "anomalyContainmentBreach",
                "observedPitGate",
                "observedFleshmassHeart",
                "taleincident",            // Uniquely owns ship launch/escape Tales.
                "romance_relation",        // Formal bond transitions.
                "progressionPsylink",      // Major pawn-status changes.
                "progressionXenotype",
                "progressionRoyalTitle",
                "progressionTraitGained"
            };
            foreach (string key in protectedGroupKeys)
            {
                AssertEqual("protected group remains essential: " + key,
                    DiaryFrequencyTiers.Essential,
                    DiaryFrequencyTiers.Normalize(ChildValue(groupByKey[key], "frequencyTier")));
            }

            AssertTrue("protected life group owns vanilla recruitment milestone",
                HasListValue(groupByKey["talelife"], "matchDefNames", "Recruited"));
            AssertTrue("protected life group owns vanilla birth milestone",
                HasListValue(groupByKey["talelife"], "matchDefNames", "GaveBirth"));
            AssertTrue("protected arrival group owns canonical faction-change arrival",
                HasListValue(groupByKey["arrival"], "matchDefNames", "PawnDiary_Arrival"));
            AssertTrue("protected Biotech birth group owns exact canonical event",
                HasListValue(groupByKey["biotechFamilyBirth"], "matchDefNames", "BiotechFamilyBirth"));
            AssertTrue("protected combat group owns canonical death fallback",
                HasListValue(groupByKey["talecombat"], "matchDefNames", "PawnDiary_DeathFallback"));
            AssertTrue("protected incident group owns ship escape",
                HasListValue(groupByKey["taleincident"], "matchDefNames", "EndGame_ShipEscape"));
            AssertTrue("protected incident group owns ship launch",
                HasListValue(groupByKey["taleincident"], "matchDefNames", "LaunchedShip"));

            string[] recipientDeathTales =
            {
                "KilledCapacity",
                "KilledLongRange",
                "KilledMajorThreat",
                "KilledMelee",
                "KilledMortar",
                "KilledChild",
                "KilledColonist",
                "KilledColonyAnimal"
            };
            AssertTrue("death initiator Tale remains classified as protected combat",
                HasListValue(groupByKey["talecombat"], "matchDefNames", "KilledBy"));
            AssertTrue("death initiator victim-role metadata remains explicit",
                HasListValue(groupByKey["talecombat"], "deathVictimInitiatorDefNames", "KilledBy"));
            foreach (string taleDefName in recipientDeathTales)
            {
                AssertTrue("death recipient Tale remains classified: " + taleDefName,
                    HasListValue(groupByKey["talecombat"], "matchDefNames", taleDefName));
                AssertTrue("death recipient victim-role metadata remains explicit: " + taleDefName,
                    HasListValue(
                        groupByKey["talecombat"],
                        "deathVictimRecipientDefNames",
                        taleDefName));
            }

            XDocument presetsDocument = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryFrequencyPresetDefs.xml"));
            XElement[] presetDefs = presetsDocument
                .Descendants("PawnDiary.DiaryFrequencyPresetDef").ToArray();
            AssertEqual("exactly Lite Standard Frequent ship as Defs", 3, presetDefs.Length);
            AssertEqual("preset keys are unique", 3,
                presetDefs.Select(def => ChildValue(def, "defName"))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count());

            string[] expectedPresetKeys =
            {
                "PawnDiary_Frequency_Lite",
                "PawnDiary_Frequency_Standard",
                "PawnDiary_Frequency_Frequent"
            };
            foreach (string presetKey in expectedPresetKeys)
            {
                XElement def = presetDefs.FirstOrDefault(candidate => string.Equals(
                    ChildValue(candidate, "defName"), presetKey, StringComparison.OrdinalIgnoreCase));
                AssertTrue("frequency preset exists: " + presetKey, def != null);
                AssertTrue("preset label is present: " + presetKey,
                    !string.IsNullOrWhiteSpace(ChildValue(def, "label")));
                AssertTrue("preset description is present: " + presetKey,
                    !string.IsNullOrWhiteSpace(ChildValue(def, "description")));

                XElement[] tierRows = def.Element("tierMultipliers")?.Elements("li").ToArray()
                    ?? Array.Empty<XElement>();
                AssertEqual("preset defines all four tiers: " + presetKey, 4, tierRows.Length);
                AssertEqual("preset tier keys are unique: " + presetKey, 4,
                    tierRows.Select(row => DiaryFrequencyTiers.Normalize(ChildValue(row, "tier")))
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count());
                foreach (XElement row in tierRows)
                {
                    string tier = ChildValue(row, "tier");
                    float multiplier = FrequencyFloat(row, "multiplier");
                    AssertTrue("preset tier token is known: " + presetKey + "/" + tier,
                        DiaryFrequencyTiers.IsKnown(tier));
                    AssertTrue("preset multiplier is finite and supported: " + presetKey + "/" + tier,
                        !float.IsNaN(multiplier)
                        && !float.IsInfinity(multiplier)
                        && multiplier >= 0f
                        && multiplier <= DiaryFrequencyPolicy.MaximumMultiplier);
                }

                XElement[] overrideRows = def.Element("groupOverrides")?.Elements("li").ToArray()
                    ?? Array.Empty<XElement>();
                AssertEqual("exact override keys are unique: " + presetKey, overrideRows.Length,
                    overrideRows.Select(row => ChildValue(row, "groupKey"))
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count());
                foreach (XElement row in overrideRows)
                {
                    string key = ChildValue(row, "groupKey");
                    float multiplier = FrequencyFloat(row, "multiplier");
                    AssertTrue("exact override refers to a shipped group: " + presetKey + "/" + key,
                        groupByKey.ContainsKey(key));
                    AssertTrue("exact override never targets External: " + presetKey + "/" + key,
                        !string.Equals(
                            ChildValue(groupByKey[key], "domain"),
                            "External",
                            StringComparison.OrdinalIgnoreCase));
                    AssertTrue("exact override multiplier is finite and supported: "
                        + presetKey + "/" + key,
                        !float.IsNaN(multiplier)
                        && !float.IsInfinity(multiplier)
                        && multiplier >= 0f
                        && multiplier <= DiaryFrequencyPolicy.MaximumMultiplier);
                }
            }

            DiaryFrequencyPresetSnapshot lite = FrequencyPresetSnapshot(
                presetDefs.First(def => ChildValue(def, "defName") == "PawnDiary_Frequency_Lite"));
            DiaryFrequencyPresetSnapshot standard = FrequencyPresetSnapshot(
                presetDefs.First(def => ChildValue(def, "defName") == "PawnDiary_Frequency_Standard"));
            DiaryFrequencyPresetSnapshot frequent = FrequencyPresetSnapshot(
                presetDefs.First(def => ChildValue(def, "defName") == "PawnDiary_Frequency_Frequent"));

            AssertNear("Lite essential is protected", 1f,
                lite.tierMultipliers[DiaryFrequencyTiers.Essential]);
            AssertNear("Lite significant baseline", 0.6f,
                lite.tierMultipliers[DiaryFrequencyTiers.Significant]);
            AssertNear("Lite routine baseline", 0.3f,
                lite.tierMultipliers[DiaryFrequencyTiers.Routine]);
            AssertNear("Lite ambient baseline", 0.15f,
                lite.tierMultipliers[DiaryFrequencyTiers.Ambient]);
            AssertEqual("Lite owns five deliberate reflection overrides", 5, lite.groupOverrides.Count);
            Dictionary<string, float> expectedLiteOverrides = new Dictionary<string, float>(
                StringComparer.OrdinalIgnoreCase)
            {
                { "dayreflection", 0.35f },
                { "quadrumreflection", 0.75f },
                { "reflectionBelief", 0.5f },
                { "socialReflection", 0.35f },
                { "reflection", 0.75f }
            };
            foreach (KeyValuePair<string, float> expected in expectedLiteOverrides)
            {
                AssertTrue("Lite reflection override exists: " + expected.Key,
                    lite.groupOverrides.ContainsKey(expected.Key));
                AssertNear("Lite reflection override value: " + expected.Key,
                    expected.Value, lite.groupOverrides[expected.Key]);
            }
            AssertNear("Frequent does not duplicate deterministic essential events", 1f,
                frequent.tierMultipliers[DiaryFrequencyTiers.Essential]);

            foreach (XElement group in playerGroups)
            {
                string key = ChildValue(group, "defName");
                string tier = ChildValue(group, "frequencyTier");
                AssertNear("Standard parity for group " + key, 1f,
                    DiaryFrequencyPolicy.ResolvePresetMultiplier(standard, key, tier));
                if (string.Equals(tier, DiaryFrequencyTiers.Essential, StringComparison.OrdinalIgnoreCase))
                {
                    AssertNear("Lite preserves essential group " + key, 1f,
                        DiaryFrequencyPolicy.ResolvePresetMultiplier(lite, key, tier));
                }
            }

            XDocument english = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected",
                "PawnDiary.DiaryFrequencyPresetDef", "DiaryFrequencyPresetDefs.xml"));
            XDocument russian = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryFrequencyPresetDef", "DiaryFrequencyPresetDefs.xml"));
            foreach (XElement def in presetDefs)
            {
                string key = ChildValue(def, "defName");
                AssertEqual("English preset label mirrors base Def: " + key,
                    ChildValue(def, "label"), KeyedValue(english, key + ".label"));
                AssertEqual("English preset description mirrors base Def: " + key,
                    ChildValue(def, "description"), KeyedValue(english, key + ".description"));
                AssertTrue("Russian preset label exists: " + key,
                    !string.IsNullOrWhiteSpace(KeyedValue(russian, key + ".label")));
                AssertTrue("Russian preset description exists: " + key,
                    !string.IsNullOrWhiteSpace(KeyedValue(russian, key + ".description")));
            }
        }

        private static void TestDiaryFrequencyLegacyNativeBaselines()
        {
            XDocument signalPolicies = XDocument.Load(
                RepoPath("1.6", "Defs", "DiarySignalPolicyDefs.xml"));
            XElement work = signalPolicies
                .Descendants("PawnDiary.DiarySignalPolicyDef")
                .First(def => ChildValue(def, "signalKey") == "Work");
            float workBase = FrequencyFloat(work, "baseChance");
            float passion = FrequencyFloat(work, "passionChanceMultiplier");
            float negative = FrequencyFloat(work, "negativeChanceMultiplier");
            float dark = FrequencyFloat(work, "darkStudyChanceMultiplier");
            float recent = FrequencyFloat(work, "recentDifferentTypeMultiplier");

            AssertNear("legacy routine Work native chance", 0.08f, workBase);
            AssertNear("legacy passionate Work native chance", 0.112f, workBase * passion);
            AssertNear("legacy strained Work native chance", 0.096f, workBase * negative);
            AssertNear("legacy dark-study Work native chance", 0.144f, workBase * negative * dark);
            AssertNear("legacy passionate dark-study Work native chance", 0.2016f,
                workBase * passion * negative * dark);
            AssertNear("recent different Work type halves native chance", 0.5f, recent);

            XDocument tuning = XDocument.Load(RepoPath("1.6", "Defs", "DiaryTuningDef.xml"));
            XElement tuningDef = tuning.Descendants("PawnDiary.DiaryTuningDef").First();
            float abilityMin = FrequencyFloat(tuningDef, "abilityUseMinChance");
            float abilityMax = FrequencyFloat(tuningDef, "abilityUseMaxChance");
            float abilityReference = FrequencyFloat(tuningDef, "abilityUseReferenceCooldownTicks");
            AssertNear("legacy no-cooldown Ability native chance", 0.03f,
                AbilityBaselineChance(0f, abilityMin, abilityMax, abilityReference));
            AssertNear("legacy short-cooldown Ability native chance", 0.0588f,
                AbilityBaselineChance(2500f, abilityMin, abilityMax, abilityReference));
            AssertNear("legacy reference-cooldown Ability native chance", 0.39f,
                AbilityBaselineChance(60000f, abilityMin, abilityMax, abilityReference));
            AssertNear("legacy long-cooldown Ability native chance", 0.606f,
                AbilityBaselineChance(240000f, abilityMin, abilityMax, abilityReference));

            List<XElement> promotionGroups = AllShippedFrequencyGroupDefs()
                .Where(group => group.Element("promotion") != null).ToList();
            string[] expectedPromotionKeys =
            {
                "smalltalk", "strangechat", "hospitality_guestwork", "rimpsyche_chatter",
                "rimtalk_chatter", "speakup_chitchat", "wbr_askedout"
            };
            AssertEqual("legacy interaction promotion has seven configured routes", 7,
                promotionGroups.Count);
            AssertEqual("legacy promotion group keys remain exact", 7,
                promotionGroups.Count(group => expectedPromotionKeys.Contains(
                    ChildValue(group, "defName"), StringComparer.OrdinalIgnoreCase)));

            foreach (XElement group in promotionGroups)
            {
                string key = ChildValue(group, "defName");
                XElement promotion = group.Element("promotion");
                bool broadCore = key == "smalltalk" || key == "strangechat";
                float expectedBase = broadCore ? 0.04f : key == "wbr_askedout" ? 0.05f : 0.005f;
                float expectedMax = broadCore ? 0.6f : 0.08f;
                float expectedStrong = broadCore ? 0.25f : 0.025f;
                float expectedOtherBonus = broadCore ? 0.2f : 0.025f;
                AssertNear("legacy promotion base chance: " + key, expectedBase,
                    FrequencyFloat(promotion, "baseChance"));
                AssertNear("legacy promotion max chance: " + key, expectedMax,
                    FrequencyFloat(promotion, "maxChance"));
                AssertNear("legacy promotion strong-opinion bonus: " + key, expectedStrong,
                    FrequencyFloat(promotion, "opinionStrongBonus"));
                AssertNear("legacy promotion asymmetry bonus: " + key, expectedOtherBonus,
                    FrequencyFloat(promotion, "opinionAsymmetryBonus"));
                AssertNear("legacy promotion low-need bonus: " + key, expectedOtherBonus,
                    FrequencyFloat(promotion, "needLowBonus"));
                AssertNear("legacy promotion low-mood bonus: " + key, expectedOtherBonus,
                    FrequencyFloat(promotion, "moodExtremeBonus"));
            }
        }

        private static void TestDiaryFrequencySyntheticAdmissionVolumeFixture()
        {
            List<XElement> groups = AllShippedFrequencyGroupDefs();
            Dictionary<string, XElement> groupByKey = groups.ToDictionary(
                group => ChildValue(group, "defName"),
                group => group,
                StringComparer.OrdinalIgnoreCase);
            XDocument presets = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryFrequencyPresetDefs.xml"));
            DiaryFrequencyPresetSnapshot standard = FrequencyPresetSnapshot(
                presets.Descendants("PawnDiary.DiaryFrequencyPresetDef")
                    .First(def => ChildValue(def, "defName") == "PawnDiary_Frequency_Standard"));
            DiaryFrequencyPresetSnapshot lite = FrequencyPresetSnapshot(
                presets.Descendants("PawnDiary.DiaryFrequencyPresetDef")
                    .First(def => ChildValue(def, "defName") == "PawnDiary_Frequency_Lite"));

            // A deterministic candidate mix: five protected raids, ten significant social events,
            // 100 Ability attempts at the shipped reference-cooldown chance (0.39), and 200 routine
            // Work attempts at the shipped base chance (0.08). Expected admitted volume is the sum
            // of clamp(native chance * preset multiplier), so no statistical/RNG assertion is needed.
            // This intentionally models source admission only; Slice 3 separately settles batching.
            string[] keys = { "raid", "romance", "abilityUsed", "workRoutine" };
            int[] occurrences = { 5, 10, 100, 200 };
            float[] nativeChances = { 1f, 1f, 0.39f, 0.08f };
            float standardVolume = 0f;
            float liteVolume = 0f;
            for (int i = 0; i < keys.Length; i++)
            {
                XElement group = groupByKey[keys[i]];
                string tier = ChildValue(group, "frequencyTier");
                standardVolume += occurrences[i] * Math.Min(
                    1f,
                    nativeChances[i]
                    * DiaryFrequencyPolicy.ResolvePresetMultiplier(standard, keys[i], tier));
                liteVolume += occurrences[i] * Math.Min(
                    1f,
                    nativeChances[i]
                    * DiaryFrequencyPolicy.ResolvePresetMultiplier(lite, keys[i], tier));
            }

            AssertNear("synthetic Standard baseline is 70 expected admitted candidates",
                70f, standardVolume);
            AssertNear("synthetic Lite fixture is 25.1 expected admitted candidates",
                25.1f, liteVolume);
            float reduction = 1f - (liteVolume / standardVolume);
            AssertTrue("Lite admission fixture measures at least a 60 percent reduction",
                reduction >= 0.60f);
            AssertTrue("Lite admission fixture remains inside the planned 60-75 percent band",
                reduction <= 0.75f);
        }

        private static List<XElement> AllShippedFrequencyGroupDefs()
        {
            string defsRoot = RepoPath("1.6", "Defs");
            List<XElement> groups = new List<XElement>();
            foreach (string path in Directory.EnumerateFiles(defsRoot, "*.xml", SearchOption.AllDirectories))
            {
                XDocument document = XDocument.Load(path);
                groups.AddRange(document.Descendants("PawnDiary.DiaryInteractionGroupDef"));
            }

            return groups;
        }

        private static DiaryFrequencyPresetSnapshot FrequencyPresetSnapshot(XElement def)
        {
            DiaryFrequencyPresetSnapshot snapshot = new DiaryFrequencyPresetSnapshot
            {
                presetKey = ChildValue(def, "defName")
            };
            foreach (XElement row in def.Element("tierMultipliers")?.Elements("li")
                ?? Enumerable.Empty<XElement>())
            {
                snapshot.tierMultipliers[ChildValue(row, "tier")] =
                    FrequencyFloat(row, "multiplier");
            }

            foreach (XElement row in def.Element("groupOverrides")?.Elements("li")
                ?? Enumerable.Empty<XElement>())
            {
                snapshot.groupOverrides[ChildValue(row, "groupKey")] =
                    FrequencyFloat(row, "multiplier");
            }

            return snapshot;
        }

        private static float FrequencyFloat(XElement parent, string childName)
        {
            float value;
            string text = ChildValue(parent, childName);
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                throw new InvalidOperationException(
                    "Expected invariant float in <" + childName + "> but found [" + text + "].");
            }

            return value;
        }

        private static float AbilityBaselineChance(
            float cooldownTicks,
            float minimum,
            float maximum,
            float referenceTicks)
        {
            float boundedCooldown = Math.Max(0f, cooldownTicks);
            return minimum + ((maximum - minimum)
                * (boundedCooldown / (boundedCooldown + Math.Max(1f, referenceTicks))));
        }
    }
}

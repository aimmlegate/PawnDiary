// Standalone, no-RimWorld checks for the pawn memory subsystem (design/MEMORY_SYSTEM_DESIGN.md):
// extraction (tags/keywords/importance/excerpt), direct similarity scoring, seeded gates, the
// 1-hop spreading-activation bridge, rendering + character budget, eviction planning, the prompt
// composer, the default policy surface, and behavioral-default parity with the shipped XML. The
// project file links only pure source, making any accidental Verse/Unity/DLC dependency a
// compile-time failure.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using PawnDiary;

namespace PawnMemoryTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestPolicyDefaultsAndTokens();
            TestPolicyXmlParity();
            TestExtractionTagMapping();
            TestExtractionKeywords();
            TestExtractionImportanceAndFragmentText();
            TestRecallGates();
            TestBlankFragmentsNeverShadowRecall();
            TestDirectScoringGolden();
            TestDirectScoringSaturationAndOverlap();
            TestDirectScoringMinAgeAndCooldown();
            TestDirectTieBreaksAndSelfRecall();
            TestSpreadAssociativeBridge();
            TestSpreadExclusions();
            TestSpreadGateSeedFlip();
            TestRenderingAgeBands();
            TestRenderingBudget();
            TestNarrativeAgeOffsetGuardAndLabel();
            TestLineCapWholePicks();
            TestDeterminismAndNoMutation();
            TestOneMechanismRoundTrip();
            TestEvictionStaleRule();
            TestEvictionCapsAndTieBreaks();
            TestEvictionGlobalCapAndNoMutation();
            TestPromptCompose();
            TestLoreSeedProvenanceAndTokens();
            TestLoreSeedEligibility();
            TestLoreSeedPlanInitialDeterminismAndCaps();
            TestLoreSeedSpecificReservationAndMutex();
            TestLoreSeedNarrativeOffsetClamp();
            TestLoreSeedPlanProgression();
            TestCoreLoreCooldownGate();
            TestEvictionLoreSuppression();
            TestAuthoredKeywordNormalization();
            TestLoreCatalogContract();
            TestLoreCatalogReachabilityAndReservation();
            TestLoreCatalogRussianParity();
            TestLoreCatalogRecallSmoke();
            Console.WriteLine("PawnMemoryTests passed " + assertions + " assertions.");
            return 0;
        }

        // ---------------------------------------------------------------------------------------------
        // Policy surface
        // ---------------------------------------------------------------------------------------------

        private static void TestPolicyDefaultsAndTokens()
        {
            string[] known =
            {
                "combat", "danger", "conflict", "breakdown", "dread", "body", "psychic", "royalty",
                "ritual", "work", "family", "romance", "death", "arrival", "illness", "joy",
                "sorrow", "social", "lore"
            };
            for (int i = 0; i < known.Length; i++)
            {
                AssertTrue("tag token known: " + known[i], MemoryTagTokens.IsKnown(known[i]));
            }

            AssertTrue("tag vocabulary is case-sensitive schema", !MemoryTagTokens.IsKnown("Combat"));
            AssertTrue("unknown tag rejected", !MemoryTagTokens.IsKnown("unknown"));
            AssertTrue("empty tag rejected", !MemoryTagTokens.IsKnown(string.Empty));
            AssertTrue("exactly nineteen tag tokens", CountKnownTokens(known) == 19);

            MemoryPolicySnapshot policy = MemoryPolicySnapshot.CreateDefault();
            AssertNear("default minDepositImportance", 0.3f, policy.minDepositImportance, 0.0001f);
            AssertEqual("default fragmentTextMaxChars", 200, policy.fragmentTextMaxChars);
            AssertEqual("default maxKeywordsPerFragment", 8, policy.maxKeywordsPerFragment);
            AssertNear("default fallbackCueImportance", 0.3f, policy.fallbackCueImportance, 0.0001f);
            AssertNear("default importantGroupBonus", 0.15f, policy.importantGroupBonus, 0.0001f);
            AssertNear("default negativeMoodBonus", 0.10f, policy.negativeMoodBonus, 0.0001f);
            AssertNear("default positiveMoodBonus", 0.05f, policy.positiveMoodBonus, 0.0001f);
            AssertEqual("default minFragmentsForRecall", 4, policy.minFragmentsForRecall);
            AssertNear("default recallGateChance", 0.6f, policy.recallGateChance, 0.0001f);
            AssertNear("default tagWeight", 0.4f, policy.tagWeight, 0.0001f);
            AssertNear("default keywordWeight", 0.6f, policy.keywordWeight, 0.0001f);
            AssertEqual("default tagSaturationCount", 2, policy.tagSaturationCount);
            AssertEqual("default keywordSaturationCount", 3, policy.keywordSaturationCount);
            AssertEqual("default recencyHalfLifeTicks", 1800000, policy.recencyHalfLifeTicks);
            AssertNear("default recencyFloor", 0.25f, policy.recencyFloor, 0.0001f);
            AssertEqual("default minRecallAgeTicks", 60000, policy.minRecallAgeTicks);
            AssertNear("default minDirectScore", 0.30f, policy.minDirectScore, 0.0001f);
            AssertNear("default spreadGateChance", 0.5f, policy.spreadGateChance, 0.0001f);
            AssertNear("default spreadDamping", 0.5f, policy.spreadDamping, 0.0001f);
            AssertNear("default minSpreadScore", 0.15f, policy.minSpreadScore, 0.0001f);
            AssertEqual("default recallCooldownTicks", 300000, policy.recallCooldownTicks);
            AssertNear("default repetitionPenaltyFactor", 0.25f, policy.repetitionPenaltyFactor, 0.0001f);
            AssertEqual("default memoryContextMaxChars", 500, policy.memoryContextMaxChars);
            AssertEqual("default memoryContextMaxLines", 2, policy.memoryContextMaxLines);
            AssertEqual("default maxFragmentsPerPawn", 60, policy.maxFragmentsPerPawn);
            AssertNear("default coreImportanceThreshold", 0.8f, policy.coreImportanceThreshold, 0.0001f);
            AssertEqual("default maxCoreFragmentsPerPawn", 15, policy.maxCoreFragmentsPerPawn);
            AssertEqual("default retentionHalfLifeTicks", 1800000, policy.retentionHalfLifeTicks);
            AssertEqual("default staleEvictTicks", 7200000, policy.staleEvictTicks);
            AssertEqual("default maxTotalFragments", 3000, policy.maxTotalFragments);
            AssertEqual("default deadOwnerGraceTicks", 3600000, policy.deadOwnerGraceTicks);
            AssertEqual("default memoryEvictionScanIntervalTicks", 150000, policy.memoryEvictionScanIntervalTicks);
            AssertEqual("default instruction empty (XML supplies it)", string.Empty, policy.memoryContextInstruction);

            AssertEqual("fourteen cue importance rows", 14, policy.cueImportance.Count);
            AssertNear("extremeDark importance", 0.9f, CueImportance(policy, "extremeDark"), 0.0001f);
            AssertNear("bodyPartLost importance", 0.85f, CueImportance(policy, "bodyPartLost"), 0.0001f);
            AssertNear("quiet importance", 0.2f, CueImportance(policy, "quiet"), 0.0001f);
            AssertEqual("fourteen cue tag rows", 14, policy.cueTags.Count);
            AssertEqual("three context marker rows", 3, policy.contextMarkerTags.Count);
            AssertTrue("weapon keyword key present", policy.contextKeywordKeys.Contains("weapon"));
            AssertEqual("four age bands", 4, policy.ageBands.Count);
            for (int i = 1; i < policy.ageBands.Count; i++)
            {
                AssertTrue("age bands ascending at " + i,
                    policy.ageBands[i].maxAgeTicks > policy.ageBands[i - 1].maxAgeTicks);
            }
        }

        /// <summary>
        /// Pins the inert layer's missing-Def guarantee: every BEHAVIORAL default in code must match
        /// the shipped XML. Natural-language memoryContextInstruction is intentionally XML-only and
        /// is asserted as the one explicit exception, so this test never encourages hardcoded prompt
        /// prose in the pure fallback.
        /// </summary>
        private static void TestPolicyXmlParity()
        {
            MemoryPolicySnapshot policy = MemoryPolicySnapshot.CreateDefault();
            string path = FindRepositoryFile(Path.Combine("1.6", "Defs", "DiaryMemoryTuningDef.xml"));
            XDocument document = XDocument.Load(path);
            XElement def = document.Root?.Element("PawnDiary.DiaryMemoryTuningDef");
            AssertTrue("memory tuning XML contains its Def", def != null);

            AssertXmlBool(def, "enabled", policy.enabled);
            AssertXmlFloat(def, "minDepositImportance", policy.minDepositImportance);
            AssertXmlInt(def, "fragmentTextMaxChars", policy.fragmentTextMaxChars);
            AssertXmlInt(def, "maxKeywordsPerFragment", policy.maxKeywordsPerFragment);
            AssertXmlFloat(def, "fallbackCueImportance", policy.fallbackCueImportance);
            AssertXmlFloat(def, "importantGroupBonus", policy.importantGroupBonus);
            AssertXmlFloat(def, "negativeMoodBonus", policy.negativeMoodBonus);
            AssertXmlFloat(def, "positiveMoodBonus", policy.positiveMoodBonus);
            AssertXmlInt(def, "minFragmentsForRecall", policy.minFragmentsForRecall);
            AssertXmlFloat(def, "recallGateChance", policy.recallGateChance);
            AssertXmlFloat(def, "tagWeight", policy.tagWeight);
            AssertXmlFloat(def, "keywordWeight", policy.keywordWeight);
            AssertXmlInt(def, "tagSaturationCount", policy.tagSaturationCount);
            AssertXmlInt(def, "keywordSaturationCount", policy.keywordSaturationCount);
            AssertXmlInt(def, "recencyHalfLifeTicks", policy.recencyHalfLifeTicks);
            AssertXmlFloat(def, "recencyFloor", policy.recencyFloor);
            AssertXmlInt(def, "minRecallAgeTicks", policy.minRecallAgeTicks);
            AssertXmlFloat(def, "minDirectScore", policy.minDirectScore);
            AssertXmlFloat(def, "spreadGateChance", policy.spreadGateChance);
            AssertXmlFloat(def, "spreadDamping", policy.spreadDamping);
            AssertXmlFloat(def, "minSpreadScore", policy.minSpreadScore);
            AssertXmlInt(def, "recallCooldownTicks", policy.recallCooldownTicks);
            AssertXmlFloat(def, "repetitionPenaltyFactor", policy.repetitionPenaltyFactor);
            AssertXmlInt(def, "memoryContextMaxChars", policy.memoryContextMaxChars);
            AssertXmlInt(def, "memoryContextMaxLines", policy.memoryContextMaxLines);
            AssertXmlBool(def, "loreSeedsEnabled", policy.loreSeedsEnabled);
            AssertXmlInt(def, "maxInitialLoreSeedsPerPawn", policy.maxInitialLoreSeedsPerPawn);
            AssertXmlInt(def, "minSpecificInitialLoreSeedsPerPawn", policy.minSpecificInitialLoreSeedsPerPawn);
            AssertXmlFloat(def, "loreSeedOrdinaryImportance", policy.loreSeedOrdinaryImportance);
            AssertXmlFloat(def, "loreSeedCoreImportance", policy.loreSeedCoreImportance);
            AssertXmlInt(def, "loreSeedNarrativeAgeOffsetTicks", policy.loreSeedNarrativeAgeOffsetTicks);
            AssertXmlInt(def, "maxCoreLoreSeedsPerPawnLifetime", policy.maxCoreLoreSeedsPerPawnLifetime);
            AssertXmlInt(def, "coreLoreRecallCooldownTicks", policy.coreLoreRecallCooldownTicks);
            AssertXmlInt(def, "maxProgressionLoreSeedsPerPawnLifetime", policy.maxProgressionLoreSeedsPerPawnLifetime);
            AssertXmlInt(def, "maxFragmentsPerPawn", policy.maxFragmentsPerPawn);
            AssertXmlFloat(def, "coreImportanceThreshold", policy.coreImportanceThreshold);
            AssertXmlInt(def, "maxCoreFragmentsPerPawn", policy.maxCoreFragmentsPerPawn);
            AssertXmlInt(def, "retentionHalfLifeTicks", policy.retentionHalfLifeTicks);
            AssertXmlInt(def, "staleEvictTicks", policy.staleEvictTicks);
            AssertXmlInt(def, "maxTotalFragments", policy.maxTotalFragments);
            AssertXmlInt(def, "deadOwnerGraceTicks", policy.deadOwnerGraceTicks);
            AssertXmlInt(def, "memoryEvictionScanIntervalTicks", policy.memoryEvictionScanIntervalTicks);

            List<XElement> cueImportanceRows = XmlRows(def, "cueImportance");
            AssertEqual("XML cue importance row count", policy.cueImportance.Count, cueImportanceRows.Count);
            for (int i = 0; i < policy.cueImportance.Count; i++)
            {
                AssertEqual("XML cue importance token " + i, policy.cueImportance[i].cue,
                    XmlValue(cueImportanceRows[i], "cue"));
                AssertNear("XML cue importance value " + i, policy.cueImportance[i].importance,
                    XmlFloat(cueImportanceRows[i], "importance"), 0.0001f);
            }

            List<XElement> cueTagRows = XmlRows(def, "cueTags");
            AssertEqual("XML cue tag row count", policy.cueTags.Count, cueTagRows.Count);
            for (int i = 0; i < policy.cueTags.Count; i++)
            {
                AssertEqual("XML cue tag token " + i, policy.cueTags[i].cue, XmlValue(cueTagRows[i], "cue"));
                AssertSequence("XML cue tags " + i, policy.cueTags[i].tags.ToArray(),
                    XmlStringList(cueTagRows[i].Element("tags")));
            }

            List<XElement> markerRows = XmlRows(def, "contextMarkerTags");
            AssertEqual("XML context marker row count", policy.contextMarkerTags.Count, markerRows.Count);
            for (int i = 0; i < policy.contextMarkerTags.Count; i++)
            {
                AssertEqual("XML context marker " + i, policy.contextMarkerTags[i].marker,
                    XmlValue(markerRows[i], "marker"));
                AssertSequence("XML context marker tags " + i, policy.contextMarkerTags[i].tags.ToArray(),
                    XmlStringList(markerRows[i].Element("tags")));
            }

            AssertSequence("XML context keyword keys", policy.contextKeywordKeys.ToArray(),
                XmlStringList(def.Element("contextKeywordKeys")));
            AssertSequence("XML progression lore event tokens",
                policy.progressionLoreSeedEventDefNames.ToArray(),
                XmlStringList(def.Element("progressionLoreSeedEventDefNames")));

            List<XElement> ageRows = XmlRows(def, "ageBands");
            AssertEqual("XML age band row count", policy.ageBands.Count, ageRows.Count);
            for (int i = 0; i < policy.ageBands.Count; i++)
            {
                AssertEqual("XML age band max " + i, policy.ageBands[i].maxAgeTicks,
                    XmlInt(ageRows[i], "maxAgeTicks"));
                AssertEqual("XML age band label " + i, policy.ageBands[i].label,
                    XmlValue(ageRows[i], "label"));
            }

            AssertEqual("code prompt instruction fallback remains blank", string.Empty,
                policy.memoryContextInstruction);
            AssertTrue("XML supplies the localized prompt instruction",
                !string.IsNullOrWhiteSpace(XmlValue(def, "memoryContextInstruction")));
        }

        // ---------------------------------------------------------------------------------------------
        // Extraction
        // ---------------------------------------------------------------------------------------------

        private static void TestExtractionTagMapping()
        {
            MemoryPolicySnapshot policy = MemoryPolicySnapshot.CreateDefault();

            AssertTags("combat cue", policy, CueInput("combat"), "combat");
            AssertTags("danger cue", policy, CueInput("danger"), "combat", "danger");
            AssertTags("socialFight cue", policy, CueInput("socialFight"), "conflict", "social");
            AssertTags("mentalBreak cue", policy, CueInput("mentalBreak"), "breakdown");
            AssertTags("extremeDark cue", policy, CueInput("extremeDark"), "dread");
            AssertTags("strangeChat cue", policy, CueInput("strangeChat"), "dread", "social");
            AssertTags("bodyPartLost cue", policy, CueInput("bodyPartLost"), "body", "illness");
            AssertTags("bodyPartAnomalous cue", policy, CueInput("bodyPartAnomalous"), "body", "illness");
            AssertTags("bodyPartArtificial cue", policy, CueInput("bodyPartArtificial"), "body", "illness");
            AssertTags("psychic cue", policy, CueInput("psychic"), "psychic");
            AssertTags("royalty cue", policy, CueInput("royalty"), "royalty");
            AssertTags("white cue", policy, CueInput("white"), "joy");
            AssertTags("daze cue", policy, CueInput("daze"), "breakdown");
            AssertTags("quiet cue has no tags", policy, CueInput("quiet"));
            AssertTags("unknown cue has no tags", policy, CueInput("polkaDot"));
            AssertTags("empty cue has no tags", policy, CueInput(string.Empty));

            MemoryExtractionInput negativeMood = CueInput(string.Empty);
            negativeMood.moodImpact = "negative";
            AssertTags("negative mood tags sorrow", policy, negativeMood, "sorrow");
            MemoryExtractionInput positiveMood = CueInput(string.Empty);
            positiveMood.moodImpact = "positive";
            AssertTags("positive mood tags joy", policy, positiveMood, "joy");
            MemoryExtractionInput neutralMood = CueInput(string.Empty);
            neutralMood.moodImpact = "neutral";
            AssertTags("neutral mood adds nothing", policy, neutralMood);

            MemoryExtractionInput ritual = CueInput(string.Empty);
            ritual.gameContext = "ritual=Funeral; royal_title=Count; ideological_role=none";
            AssertTags("ritual context tags ritual and royalty", policy, ritual, "ritual", "royalty");
            MemoryExtractionInput noTitle = CueInput(string.Empty);
            noTitle.gameContext = "ritual=Funeral; royal_title=none";
            AssertTags("sentinel royal_title=none never tags royalty", policy, noTitle, "ritual");
            MemoryExtractionInput psychicRitual = CueInput(string.Empty);
            psychicRitual.gameContext = "psychic_ritual=VoidProvocation; outcome=finished";
            AssertTags("psychic ritual tags ritual and psychic", policy, psychicRitual, "ritual", "psychic");

            MemoryExtractionInput pairwise = CueInput(string.Empty);
            pairwise.solo = false;
            pairwise.otherName = "Yorick";
            AssertTags("pairwise always adds social", policy, pairwise, "social");

            MemoryExtractionInput combined = CueInput("danger");
            combined.solo = false;
            combined.otherName = "Yorick";
            combined.moodImpact = "negative";
            AssertTags("danger + negative + pairwise dedupes and orders",
                policy, combined, "combat", "danger", "sorrow", "social");

            // Unknown tags authored in a policy row are dropped, never deposited.
            MemoryPolicySnapshot typoPolicy = MemoryPolicySnapshot.CreateDefault();
            typoPolicy.cueTags[0].tags.Add("not-a-real-tag");
            MemoryExtractionResult typoResult = MemoryExtraction.Extract(CueInput("extremeDark"), typoPolicy);
            AssertSequence("unknown policy tag dropped", new[] { "dread" }, typoResult.tags);
        }

        private static void TestExtractionKeywords()
        {
            MemoryPolicySnapshot policy = MemoryPolicySnapshot.CreateDefault();

            MemoryExtractionResult name = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                otherName = "  YORICK  ",
                solo = false
            }, policy);
            AssertSequence("other name normalizes to lowercase keyword", new[] { "yorick" }, name.keywords);

            MemoryExtractionResult hyphenated = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                otherName = "Mary-Anne",
                solo = false
            }, policy);
            AssertSequence("punctuation splits keywords", new[] { "mary", "anne" }, hyphenated.keywords);

            MemoryExtractionResult shortIdentity = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                otherName = "Bo",
                solo = false
            }, policy);
            AssertSequence("short pawn names remain identity keywords", new[] { "bo" }, shortIdentity.keywords);

            MemoryExtractionResult stopwordIdentity = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                otherName = "Will",
                solo = false
            }, policy);
            AssertSequence("pawn names are not prose stopwords", new[] { "will" }, stopwordIdentity.keywords);

            MemoryExtractionResult cjkIdentity = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                otherName = "李",
                solo = false
            }, policy);
            AssertSequence("one-character CJK pawn names remain identity keywords",
                new[] { "李" }, cjkIdentity.keywords);

            MemoryExtractionResult stopwords = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                solo = true,
                rawText = "The raid was over"
            }, policy);
            AssertSequence("stopwords dropped from raw text", new[] { "raid" }, stopwords.keywords);

            MemoryExtractionResult povExcluded = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                solo = true,
                rawText = "Daisy wept quietly."
            }, policy);
            AssertTrue("writer's own name never becomes a keyword",
                !povExcluded.keywords.Contains("daisy"));

            MemoryExtractionResult dedupe = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                otherName = "Yorick",
                solo = false,
                rawText = "Yorick fell during the raid."
            }, policy);
            int yorickCount = 0;
            for (int i = 0; i < dedupe.keywords.Count; i++)
            {
                if (dedupe.keywords[i] == "yorick") yorickCount++;
            }

            AssertEqual("keywords dedupe across sources", 1, yorickCount);

            MemoryExtractionResult priority = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Someone",
                otherName = "Yorick",
                solo = false,
                gameContext = "weapon=Charge rifle; royal_title=none",
                interactionLabel = "Deep conversation",
                rawText = "They talked about cooking and farming and hiking and reading."
            }, policy);
            AssertSequence("keyword priority: other name, context values, label, raw text, capped at 8",
                new[] { "yorick", "charge", "rifle", "deep", "conversation", "talked", "cooking", "farming" },
                priority.keywords);

            MemoryPolicySnapshot tightPolicy = MemoryPolicySnapshot.CreateDefault();
            tightPolicy.maxKeywordsPerFragment = 3;
            MemoryExtractionResult capped = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Someone",
                otherName = "Yorick",
                solo = false,
                gameContext = "weapon=Charge rifle",
                interactionLabel = "Deep conversation"
            }, tightPolicy);
            AssertSequence("policy keyword cap respected",
                new[] { "yorick", "charge", "rifle" }, capped.keywords);

            MemoryExtractionResult sentinel = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                solo = true,
                gameContext = "weapon=none; royal_title=n/a"
            }, policy);
            AssertEqual("sentinel context values contribute no keywords", 0, sentinel.keywords.Count);

            AssertTrue("none is a sentinel", MemoryExtraction.IsSentinelValue("none"));
            AssertTrue("NONE is a sentinel", MemoryExtraction.IsSentinelValue("NONE"));
            AssertTrue("n/a is a sentinel", MemoryExtraction.IsSentinelValue("n/a"));
            AssertTrue("unknown is a sentinel", MemoryExtraction.IsSentinelValue("unknown"));
            AssertTrue("empty is a sentinel", MemoryExtraction.IsSentinelValue(string.Empty));
            AssertTrue("a real title is not a sentinel", !MemoryExtraction.IsSentinelValue("Count"));
        }

        private static void TestExtractionImportanceAndFragmentText()
        {
            MemoryPolicySnapshot policy = MemoryPolicySnapshot.CreateDefault();
            AssertImportance("extremeDark", policy, 0.9f);
            AssertImportance("bodyPartLost", policy, 0.85f);
            AssertImportance("danger", policy, 0.8f);
            AssertImportance("combat", policy, 0.75f);
            AssertImportance("mentalBreak", policy, 0.7f);
            AssertImportance("bodyPartAnomalous", policy, 0.7f);
            AssertImportance("psychic", policy, 0.65f);
            AssertImportance("bodyPartArtificial", policy, 0.6f);
            AssertImportance("royalty", policy, 0.6f);
            AssertImportance("socialFight", policy, 0.55f);
            AssertImportance("white", policy, 0.5f);
            AssertImportance("daze", policy, 0.5f);
            AssertImportance("strangeChat", policy, 0.35f);
            AssertImportance("quiet", policy, 0.2f);
            AssertImportance("unknown cue falls back", policy, 0.3f, cue: "polkaDot");
            AssertImportance("empty cue falls back", policy, 0.3f, cue: string.Empty);

            MemoryExtractionInput important = CueInput("combat");
            important.importantGroup = true;
            AssertNear("important group bonus", 0.9f,
                MemoryExtraction.Extract(important, policy).importance, 0.0001f);

            MemoryExtractionInput negative = CueInput("danger");
            negative.moodImpact = "negative";
            AssertNear("negative mood bonus beats positive", 0.9f,
                MemoryExtraction.Extract(negative, policy).importance, 0.0001f);

            MemoryExtractionInput positive = CueInput("danger");
            positive.moodImpact = "positive";
            AssertNear("positive mood bonus", 0.85f,
                MemoryExtraction.Extract(positive, policy).importance, 0.0001f);

            MemoryExtractionInput stacked = CueInput("extremeDark");
            stacked.importantGroup = true;
            stacked.moodImpact = "negative";
            AssertNear("importance clamps at 1.0", 1.0f,
                MemoryExtraction.Extract(stacked, policy).importance, 0.0001f);

            MemoryPolicySnapshot floorPolicy = MemoryPolicySnapshot.CreateDefault();
            floorPolicy.cueImportance.Clear();
            floorPolicy.cueImportance.Add(new MemoryCueImportance { cue = "quiet", importance = 0f });
            floorPolicy.fallbackCueImportance = 0f;
            AssertNear("importance clamps at 0.05 floor", 0.05f,
                MemoryExtraction.Extract(CueInput("quiet"), floorPolicy).importance, 0.0001f);

            MemoryExtractionResult excerpt = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                solo = true,
                rawText = new string('x', 500)
            }, policy);
            AssertEqual("fragment text capped at 200 chars", 200, excerpt.fragmentText.Length);
            AssertTrue("capped excerpt ends with ellipsis", excerpt.fragmentText.EndsWith("...", StringComparison.Ordinal));

            MemoryExtractionResult firstSentence = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                solo = true,
                rawText = "First sentence. Second sentence goes on and on."
            }, policy);
            AssertEqual("excerpt keeps the first sentence", "First sentence.", firstSentence.fragmentText);

            MemoryExtractionResult emptyText = MemoryExtraction.Extract(CueInput("combat"), policy);
            AssertEqual("empty raw text yields empty excerpt", string.Empty, emptyText.fragmentText);

            MemoryExtractionResult nullInput = MemoryExtraction.Extract(null, policy);
            AssertEqual("null input yields no tags", 0, nullInput.tags.Count);
            AssertNear("null input yields zero importance", 0f, nullInput.importance, 0.0001f);
        }

        // ---------------------------------------------------------------------------------------------
        // Recall gates
        // ---------------------------------------------------------------------------------------------

        private static void TestRecallGates()
        {
            MemoryPolicySnapshot policy = MemoryPolicySnapshot.CreateDefault();
            List<MemoryFragmentSnapshot> store = BridgeStore();

            MemoryRecallResult nullQuery = MemoryRecallSelector.Recall(null, store, policy);
            AssertEqual("null query yields no picks", 0, nullQuery.picks.Count);
            AssertContains("null query diagnostic", nullQuery.diagnostics, MemoryDiagnosticTokens.NoQuery);

            MemoryPolicySnapshot disabled = MemoryPolicySnapshot.CreateDefault();
            disabled.enabled = false;
            MemoryRecallResult disabledResult = MemoryRecallSelector.Recall(
                Query(Tags("combat"), Tags(), 2060000), store, disabled);
            AssertEqual("disabled policy yields no picks", 0, disabledResult.picks.Count);
            AssertContains("disabled policy diagnostic", disabledResult.diagnostics, MemoryDiagnosticTokens.PolicyDisabled);

            List<MemoryFragmentSnapshot> small = new List<MemoryFragmentSnapshot>
            {
                Frag("m1", 2000000, Tags("combat"), Tags("yorick"), 0.8f, "text one"),
                Frag("m2", 2000000, Tags("work"), Tags("fields"), 0.5f, "text two"),
                Frag("m3", 2000000, Tags("joy"), Tags("party"), 0.5f, "text three")
            };
            MemoryRecallResult smallStore = MemoryRecallSelector.Recall(
                Query(Tags("combat"), Tags("yorick"), 2060000), small, policy);
            AssertEqual("store below minimum yields no picks", 0, smallStore.picks.Count);
            AssertContains("store size diagnostic", smallStore.diagnostics, MemoryDiagnosticTokens.StoreTooSmall);

            small.Add(null);
            small.Add(Frag(" ", 2000000, Tags("combat"), Tags("yorick"), 0.8f, "text four"));
            MemoryRecallResult stillSmall = MemoryRecallSelector.Recall(
                Query(Tags("combat"), Tags("yorick"), 2060000), small, policy);
            AssertContains("null and blank-id rows do not count toward the store minimum",
                stillSmall.diagnostics, MemoryDiagnosticTokens.StoreTooSmall);

            MemoryRecallResult nullStore = MemoryRecallSelector.Recall(
                Query(Tags("combat"), Tags(), 2060000), null, policy);
            AssertContains("null store yields store-too-small", nullStore.diagnostics, MemoryDiagnosticTokens.StoreTooSmall);

            MemoryPolicySnapshot neverRecall = MemoryPolicySnapshot.CreateDefault();
            neverRecall.recallGateChance = 0f;
            MemoryRecallResult gated = MemoryRecallSelector.Recall(
                Query(Tags("combat"), Tags(), 2060000), store, neverRecall);
            AssertEqual("zero recall chance always gates out", 0, gated.picks.Count);
            AssertContains("recall gate diagnostic", gated.diagnostics, MemoryDiagnosticTokens.RecallGateMiss);
        }

        private static void TestBlankFragmentsNeverShadowRecall()
        {
            MemoryPolicySnapshot policy = RecallPolicy();
            policy.minFragmentsForRecall = 1;
            List<MemoryFragmentSnapshot> store = new List<MemoryFragmentSnapshot>
            {
                // This row would win on score/recency if blank text were allowed to compete.
                Frag("blank-winner", 1000, Tags("combat", "danger"), Tags("yorick", "rifle", "fire"),
                    1.0f, "   "),
                Frag("renderable-runner-up", 900, Tags("combat", "danger"), Tags("yorick", "rifle"),
                    0.8f, "A renderable memory.")
            };

            MemoryRecallResult recalled = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 70000), store, policy);
            AssertEqual("blank fragments cannot occupy the direct slot", 1, recalled.picks.Count);
            AssertEqual("the best renderable fragment wins instead", "renderable-runner-up",
                recalled.picks[0].memoryId);
            AssertTrue("the surviving direct pick renders",
                recalled.memoryContext.Contains("A renderable memory."));

            policy.minFragmentsForRecall = 2;
            MemoryRecallResult tooSmall = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick"), 70000), store, policy);
            AssertContains("blank fragments do not satisfy the store-size gate",
                tooSmall.diagnostics, MemoryDiagnosticTokens.StoreTooSmall);
        }

        // ---------------------------------------------------------------------------------------------
        // Direct scoring
        // ---------------------------------------------------------------------------------------------

        private static void TestDirectScoringGolden()
        {
            MemoryPolicySnapshot policy = RecallPolicy();
            List<MemoryFragmentSnapshot> store = new List<MemoryFragmentSnapshot>
            {
                Frag("m1", 100000, Tags("combat", "danger"), Tags("yorick"), 0.5f,
                    "The raid where Yorick fell.", sourceEventId: "evt-raid"),
                Frag("f1", 100000, Tags("work"), Tags("fields"), 0.4f, "Working the fields."),
                Frag("f2", 100000, Tags("illness"), Tags("flu"), 0.4f, "A bad flu week."),
                Frag("f3", 100000, Tags("family"), Tags("mother"), 0.4f, "A letter from mother.")
            };

            // One exact half-life of age: decay is exactly 0.5.
            MemoryRecallResult result = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick"), 1900000), store, policy);
            AssertEqual("one direct pick", 1, result.picks.Count);
            AssertEqual("direct pick id", "m1", result.picks[0].memoryId);
            AssertEqual("direct pick kind", MemoryRecallPick.Direct, result.picks[0].kind);
            // base = 0.4*1 + 0.6*(1/3) = 0.6; salience = 1.0; decay = 0.5 -> 0.3 exactly.
            AssertNear("golden direct score", 0.3f, result.picks[0].score, 0.0001f);
            AssertEqual("rendered context with age band",
                "- (a few quadrums ago) The raid where Yorick fell.", result.memoryContext);
            AssertContains("selected diagnostic", result.diagnostics, MemoryDiagnosticTokens.SelectedDirect + ":m1");

            MemoryRecallResult belowThreshold = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 1900000), store, policy);
            AssertEqual("keywordless query drops below the direct threshold", 0, belowThreshold.picks.Count);
            AssertContains("no direct pick diagnostic", belowThreshold.diagnostics, MemoryDiagnosticTokens.NoDirectPick);
        }

        private static void TestDirectScoringSaturationAndOverlap()
        {
            MemoryPolicySnapshot policy = RecallPolicy();
            List<MemoryFragmentSnapshot> store = new List<MemoryFragmentSnapshot>
            {
                Frag("m1", 0, Tags("combat"), Tags(), 1.0f, "A skirmish memory."),
                Frag("f1", 0, Tags("work"), Tags("fields"), 0.4f, "Working the fields."),
                Frag("f2", 0, Tags("illness"), Tags("flu"), 0.4f, "A bad flu week."),
                Frag("f3", 0, Tags("family"), Tags("mother"), 0.4f, "A letter from mother.")
            };

            // One shared tag -> tagScore 0.5 -> base 0.2; salience 1.5; decay 0.5 -> 0.15 < 0.30.
            MemoryRecallResult halfTag = MemoryRecallSelector.Recall(
                Query(Tags("combat"), Tags(), 1800000), store, policy);
            AssertEqual("single shared tag cannot reach the threshold", 0, halfTag.picks.Count);

            // Adding three shared keywords saturates the keyword score: base 0.2+0.6 -> 0.6 score.
            store[0].keywords.AddRange(Tags("yorick", "rifle", "fire"));
            MemoryRecallResult saturated = MemoryRecallSelector.Recall(
                Query(Tags("combat"), Tags("yorick", "rifle", "fire"), 1800000), store, policy);
            AssertEqual("saturated overlaps pick", 1, saturated.picks.Count);
            AssertNear("saturated score", 0.6f, saturated.picks[0].score, 0.0001f);

            MemoryRecallResult zeroOverlap = MemoryRecallSelector.Recall(
                Query(Tags("dread"), Tags("monolith"), 1800000), store, policy);
            AssertEqual("zero overlap early-out yields no pick", 0, zeroOverlap.picks.Count);
            AssertContains("zero overlap diagnostic", zeroOverlap.diagnostics, MemoryDiagnosticTokens.NoDirectPick);
        }

        private static void TestDirectScoringMinAgeAndCooldown()
        {
            MemoryPolicySnapshot policy = RecallPolicy();
            List<MemoryFragmentSnapshot> store = PerfectOverlapStore();

            // Exactly at the minimum age the fragment is eligible again.
            MemoryRecallResult atMinAge = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 61000), store, policy);
            AssertEqual("fragment at the minimum recall age is eligible", 1, atMinAge.picks.Count);
            AssertNear("min-age score", 1.4657f, atMinAge.picks[0].score, 0.001f);

            MemoryRecallResult belowMinAge = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 60999), store, policy);
            AssertEqual("fragment one tick below the minimum age scores zero", 0, belowMinAge.picks.Count);

            // One exact half-life old: unpenalized score 0.75.
            MemoryRecallResult unpenalized = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 1801000), store, policy);
            AssertEqual("unrecalled fragment at one half-life", 1, unpenalized.picks.Count);
            AssertNear("unpenalized half-life score", 0.75f, unpenalized.picks[0].score, 0.0001f);

            // Recalled 299999 ticks ago: inside the 300000 cooldown -> quarter strength 0.1875.
            store[0].lastRecalledTick = 1501001;
            MemoryRecallResult insideCooldown = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 1801000), store, policy);
            AssertEqual("cooldown penalty suppresses a quartered score", 0, insideCooldown.picks.Count);

            // Recalled exactly 300000 ticks ago: cooldown expired (strictly-less-than window).
            store[0].lastRecalledTick = 1501000;
            MemoryRecallResult cooldownExpired = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 1801000), store, policy);
            AssertEqual("cooldown boundary is not penalized", 1, cooldownExpired.picks.Count);
            AssertNear("cooldown-expired score", 0.75f, cooldownExpired.picks[0].score, 0.0001f);
        }

        private static void TestDirectTieBreaksAndSelfRecall()
        {
            MemoryPolicySnapshot policy = RecallPolicy();
            List<MemoryFragmentSnapshot> store = new List<MemoryFragmentSnapshot>
            {
                Frag("older", 100000, Tags("combat", "danger"), Tags("yorick"), 0.5f, "Older memory."),
                Frag("newer", 200000, Tags("combat", "danger"), Tags("yorick"), 0.5f, "Newer memory."),
                Frag("f1", 100000, Tags("work"), Tags("fields"), 0.4f, "Working the fields."),
                Frag("f2", 100000, Tags("illness"), Tags("flu"), 0.4f, "A bad flu week.")
            };

            MemoryRecallResult newerWins = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick"), 2000000), store, policy);
            AssertEqual("equal scores pick the newer fragment", "newer", newerWins.picks[0].memoryId);

            store[0].createdTick = 200000;
            store[1].createdTick = 200000;
            store[0].memoryId = "m-b";
            store[1].memoryId = "m-a";
            MemoryRecallResult idWins = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick"), 2000000), store, policy);
            AssertEqual("equal scores and ticks pick the ordinal-first id", "m-a", idWins.picks[0].memoryId);

            List<MemoryFragmentSnapshot> selfStore = new List<MemoryFragmentSnapshot>
            {
                Frag("self", 0, Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 1.0f,
                    "The current event's own fragment.", sourceEventId: "evt-now"),
                Frag("other", 0, Tags("combat", "danger"), Tags(), 1.0f, "An older combat memory.",
                    sourceEventId: "evt-old"),
                Frag("f1", 0, Tags("work"), Tags("fields"), 0.4f, "Working the fields."),
                Frag("f2", 0, Tags("illness"), Tags("flu"), 0.4f, "A bad flu week.")
            };
            MemoryRecallResult selfExcluded = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 1800000, currentEventId: "evt-now"),
                selfStore, policy);
            AssertEqual("the current event can never recall its own fragment", "other",
                selfExcluded.picks[0].memoryId);
        }

        // ---------------------------------------------------------------------------------------------
        // Spreading activation
        // ---------------------------------------------------------------------------------------------

        private static void TestSpreadAssociativeBridge()
        {
            MemoryPolicySnapshot policy = RecallPolicy(recallGate: 1f, spreadGate: 1f);
            List<MemoryFragmentSnapshot> store = BridgeStore();

            MemoryRecallResult result = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000), store, policy);
            AssertEqual("bridge yields two picks", 2, result.picks.Count);
            AssertEqual("direct pick is the raid", "raid", result.picks[0].memoryId);
            AssertEqual("direct pick kind", MemoryRecallPick.Direct, result.picks[0].kind);
            AssertNear("direct raid score", 0.508123f, result.picks[0].score, 0.001f);
            AssertEqual("associative pick is the cooking memory", "cooking", result.picks[1].memoryId);
            AssertEqual("associative pick kind", MemoryRecallPick.Associative, result.picks[1].kind);
            AssertNear("associative hop score", 0.293148f, result.picks[1].score, 0.001f);
            AssertContains("associative diagnostic", result.diagnostics,
                MemoryDiagnosticTokens.SelectedAssociative + ":cooking");
            AssertTrue("context renders direct first",
                result.memoryContext.IndexOf("The raid where Yorick fell.", StringComparison.Ordinal)
                < result.memoryContext.IndexOf("Yorick showing me his rifle collection.", StringComparison.Ordinal));
            AssertTrue("associative line carries an age label",
                result.memoryContext.Contains("- (a few days ago) Yorick showing me his rifle collection."));
        }

        private static void TestSpreadExclusions()
        {
            MemoryPolicySnapshot policy = RecallPolicy(recallGate: 1f, spreadGate: 1f);
            List<MemoryFragmentSnapshot> store = BridgeStore();

            // A second DIRECT match (score >= minDirectScore against the original query) must never
            // become the hop pick even when the expanded query would rank it highest.
            store.Add(Frag("skirmish", 2000000, Tags("combat", "danger"), Tags("yorick", "rifle", "ammo"),
                0.5f, "A later skirmish with the same crew.", sourceEventId: "evt-skirmish"));
            // A fragment from the SAME source event as the direct pick is excluded from the hop.
            store.Add(Frag("echo", 2000000, Tags("death"), Tags("yorick", "rifle"),
                1.0f, "Another page from the same raid.", sourceEventId: "evt-raid"));

            MemoryRecallResult result = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000), store, policy);
            AssertEqual("still two picks", 2, result.picks.Count);
            AssertEqual("direct pick remains the raid", "raid", result.picks[0].memoryId);
            AssertEqual("hop excludes direct matches and same-source fragments", "cooking",
                result.picks[1].memoryId);

            MemoryPolicySnapshot noSpread = RecallPolicy(recallGate: 1f, spreadGate: 0f);
            MemoryRecallResult gated = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000), store, noSpread);
            AssertEqual("spread gate miss yields the direct pick only", 1, gated.picks.Count);
            AssertContains("spread gate diagnostic", gated.diagnostics, MemoryDiagnosticTokens.SpreadGateMiss);

            MemoryRecallResult noDirect = MemoryRecallSelector.Recall(
                Query(Tags("dread"), Tags("monolith"), 2060000), store, policy);
            AssertEqual("no direct pick means no spread is attempted", 0, noDirect.picks.Count);
            AssertContains("no direct diagnostic", noDirect.diagnostics, MemoryDiagnosticTokens.NoDirectPick);
            AssertTrue("no associative pick without a direct anchor",
                !ContainsPrefix(noDirect.diagnostics, MemoryDiagnosticTokens.SelectedAssociative));
        }

        private static void TestSpreadGateSeedFlip()
        {
            MemoryPolicySnapshot policy = RecallPolicy(recallGate: 1f, spreadGate: 0.5f);
            List<MemoryFragmentSnapshot> store = BridgeStore();

            int spreadYes = SeedWhere(r =>
            {
                r.NextDouble(); // recall gate draw (always passes at chance 1.0)
                return r.NextDouble() < 0.5;
            });
            int spreadNo = SeedWhere(r =>
            {
                r.NextDouble();
                return r.NextDouble() >= 0.5;
            });

            MemoryRecallResult opened = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000, seed: spreadYes), store, policy);
            AssertEqual("a passing spread roll surfaces the hop", 2, opened.picks.Count);

            MemoryRecallResult closed = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000, seed: spreadNo), store, policy);
            AssertEqual("a failing spread roll keeps only the direct pick", 1, closed.picks.Count);
            AssertContains("spread miss diagnostic", closed.diagnostics, MemoryDiagnosticTokens.SpreadGateMiss);
        }

        // ---------------------------------------------------------------------------------------------
        // Rendering
        // ---------------------------------------------------------------------------------------------

        private static void TestRenderingAgeBands()
        {
            MemoryPolicySnapshot policy = RecallPolicy();
            AssertAgeLabel(policy, 300000, "a few days ago");
            AssertAgeLabel(policy, 300001, "a couple of weeks ago");
            AssertAgeLabel(policy, 900000, "a couple of weeks ago");
            AssertAgeLabel(policy, 900001, "a few quadrums ago");
            AssertAgeLabel(policy, 3600000, "a few quadrums ago");
            AssertAgeLabel(policy, 3600001, "a long time ago");
        }

        private static void TestRenderingBudget()
        {
            MemoryPolicySnapshot policy = RecallPolicy(recallGate: 1f, spreadGate: 1f);
            List<MemoryFragmentSnapshot> store = BridgeStore();
            store[0].text = new string('d', 100);   // raid (direct)
            store[1].text = new string('c', 100);   // cooking (associative)
            string directLine = "- (a few days ago) " + new string('d', 100);
            string hopLine = "- (a few days ago) " + new string('c', 100);
            string both = directLine + "\n" + hopLine;
            AssertEqual("test budget arithmetic holds", 239, both.Length);

            MemoryPolicySnapshot roomy = RecallPolicy(recallGate: 1f, spreadGate: 1f);
            roomy.memoryContextMaxChars = 239;
            MemoryRecallResult fits = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000), store, roomy);
            AssertEqual("both picks fit the exact budget", 2, fits.picks.Count);
            AssertEqual("full context when both fit", both, fits.memoryContext);

            MemoryPolicySnapshot tight = RecallPolicy(recallGate: 1f, spreadGate: 1f);
            tight.memoryContextMaxChars = 200;
            MemoryRecallResult dropped = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000), store, tight);
            AssertEqual("over budget drops the associative pick first", 1, dropped.picks.Count);
            AssertEqual("the direct pick is never truncated mid-text", directLine, dropped.memoryContext);
            AssertContains("associative drop diagnostic", dropped.diagnostics,
                MemoryDiagnosticTokens.BudgetDroppedAssociative);

            MemoryPolicySnapshot tiny = RecallPolicy(recallGate: 1f, spreadGate: 1f);
            tiny.memoryContextMaxChars = 100;
            MemoryRecallResult emptied = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000), store, tiny);
            AssertEqual("an unaffordable direct pick is dropped too", 0, emptied.picks.Count);
            AssertEqual("empty context when nothing fits", string.Empty, emptied.memoryContext);
            AssertContains("direct drop diagnostic", emptied.diagnostics,
                MemoryDiagnosticTokens.BudgetDroppedDirect);

            MemoryRecallResult nothingRecalled = MemoryRecallSelector.Recall(
                Query(Tags("dread"), Tags(), 2060000), store, policy);
            AssertEqual("no picks renders an empty string", string.Empty, nothingRecalled.memoryContext);
        }

        /// <summary>
        /// LORE_MEMORY_SEED_PLAN §3.1: the narrative-age offset satisfies the minimum-age guard and
        /// picks the rendered age band, while recency decay keeps using real ticks — a lore seed can
        /// surface on the first prompt as an "old" memory without ever looking stale to scoring.
        /// </summary>
        private static void TestNarrativeAgeOffsetGuardAndLabel()
        {
            MemoryPolicySnapshot policy = RecallPolicy();
            List<MemoryFragmentSnapshot> store = PerfectOverlapStore();

            MemoryRecallResult gated = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 60999), store, policy);
            AssertEqual("real age below the minimum stays gated without an offset", 0, gated.picks.Count);

            store[0].narrativeAgeOffsetTicks = 1;
            MemoryRecallResult eligible = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 60999), store, policy);
            AssertEqual("narrative offset satisfies the minimum-age guard", 1, eligible.picks.Count);
            // Decay still runs on the REAL 59999-tick age: 1.5 * 0.5^(59999/1800000).
            AssertNear("recency decay stays on real age", 1.46574f, eligible.picks[0].score, 0.001f);

            store[0].narrativeAgeOffsetTicks = -500000;
            MemoryRecallResult negative = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 60999), store, policy);
            AssertEqual("negative offsets clamp to zero effect", 0, negative.picks.Count);

            // The rendered band follows the narrative age; the score does not move at all.
            const int current = 10000000;
            List<MemoryFragmentSnapshot> aged = new List<MemoryFragmentSnapshot>
            {
                Frag("seed", current - 100000, Tags("combat", "danger"), Tags("yorick", "rifle", "fire"),
                    1.0f, "The elders' story."),
                Frag("f1", current - 100000, Tags("work"), Tags("fields"), 0.4f, "Working the fields."),
                Frag("f2", current - 100000, Tags("illness"), Tags("flu"), 0.4f, "A bad flu week."),
                Frag("f3", current - 100000, Tags("family"), Tags("mother"), 0.4f, "A letter from mother.")
            };
            MemoryRecallResult lived = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), current), aged, policy);
            AssertEqual("zero offset renders the real-age band",
                "- (a few days ago) The elders' story.", lived.memoryContext);

            aged[0].narrativeAgeOffsetTicks = 7200000;
            MemoryRecallResult loreAged = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), current), aged, policy);
            AssertEqual("offset renders the narrative-age band",
                "- (a long time ago) The elders' story.", loreAged.memoryContext);
            AssertNear("offset never changes the score",
                lived.picks[0].score, loreAged.picks[0].score, 0.0000001f);
        }

        /// <summary>
        /// LORE_MEMORY_SEED_PLAN §9: the universal whole-line ceiling drops complete picks
        /// (associative first) so the surviving-pick list always equals the delivered lines.
        /// </summary>
        private static void TestLineCapWholePicks()
        {
            List<MemoryFragmentSnapshot> store = BridgeStore();

            MemoryPolicySnapshot oneLine = RecallPolicy(recallGate: 1f, spreadGate: 1f);
            oneLine.memoryContextMaxLines = 1;
            MemoryRecallResult capped = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000), store, oneLine);
            AssertEqual("one-line cap keeps only the direct pick", 1, capped.picks.Count);
            AssertEqual("the direct pick survives the cap", "raid", capped.picks[0].memoryId);
            AssertTrue("exactly one whole line is delivered",
                !capped.memoryContext.Contains("\n")
                && capped.memoryContext.Contains("The raid where Yorick fell."));
            AssertContains("line-cap diagnostic", capped.diagnostics,
                MemoryDiagnosticTokens.LineCapDroppedPick);

            MemoryPolicySnapshot defaultCap = RecallPolicy(recallGate: 1f, spreadGate: 1f);
            MemoryRecallResult both = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000), store, defaultCap);
            AssertEqual("the default two-line cap keeps the bridge outcome", 2, both.picks.Count);

            MemoryPolicySnapshot zero = RecallPolicy(recallGate: 1f, spreadGate: 1f);
            zero.memoryContextMaxLines = 0;
            MemoryRecallResult cleared = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000), store, zero);
            AssertEqual("a zero cap surfaces nothing", 0, cleared.picks.Count);
            AssertEqual("a zero cap renders an empty context", string.Empty, cleared.memoryContext);
        }

        private static void TestDeterminismAndNoMutation()
        {
            MemoryPolicySnapshot policy = RecallPolicy(recallGate: 1f, spreadGate: 1f);
            List<MemoryFragmentSnapshot> store = BridgeStore();
            MemoryRecallQuery query = Query(Tags("combat", "danger"), Tags(), 2060000, seed: 4242);

            MemoryRecallResult first = MemoryRecallSelector.Recall(query, store, policy);
            MemoryRecallResult second = MemoryRecallSelector.Recall(query, store, policy);
            AssertEqual("identical inputs render byte-identical context", first.memoryContext, second.memoryContext);
            AssertEqual("identical inputs pick identical memories", first.picks.Count, second.picks.Count);
            for (int i = 0; i < first.picks.Count; i++)
            {
                AssertEqual("pick id stable at " + i, first.picks[i].memoryId, second.picks[i].memoryId);
                AssertNear("pick score stable at " + i, first.picks[i].score, second.picks[i].score, 0f);
            }

            for (int i = 0; i < store.Count; i++)
            {
                AssertEqual("recall never bumps recall bookkeeping on " + store[i].memoryId,
                    store[i].createdTick, store[i].lastRecalledTick);
                AssertEqual("recall never counts recalls on " + store[i].memoryId, 0, store[i].recallCount);
            }

            MemoryPolicySnapshot defaultGate = MemoryPolicySnapshot.CreateDefault();
            int passing = SeedWhere(r => r.NextDouble() < 0.6);
            int failing = SeedWhere(r => r.NextDouble() >= 0.6);
            MemoryRecallResult opened = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000, seed: passing), store, defaultGate);
            AssertTrue("a passing recall roll proceeds", opened.picks.Count > 0);
            MemoryRecallResult closed = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags(), 2060000, seed: failing), store, defaultGate);
            AssertEqual("a failing recall roll returns empty", 0, closed.picks.Count);
            AssertContains("recall miss diagnostic", closed.diagnostics, MemoryDiagnosticTokens.RecallGateMiss);
        }

        private static void TestOneMechanismRoundTrip()
        {
            // The SAME extraction produces the fragment at deposit time and the query at recall
            // time, so a similar later event surfaces the earlier fragment with no second vocabulary.
            MemoryPolicySnapshot policy = RecallPolicy(recallGate: 1f, spreadGate: 0f);
            MemoryExtractionResult raidExtraction = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                otherName = "Yorick",
                colorCue = "danger",
                solo = false,
                rawText = "Raiders came with rifles."
            }, policy);
            AssertSequence("deposit extraction tags", new[] { "combat", "danger", "social" }, raidExtraction.tags);
            AssertEqual("deposit excerpt", "Raiders came with rifles.", raidExtraction.fragmentText);
            AssertTrue("deposit importance clears the noise gate",
                raidExtraction.importance >= policy.minDepositImportance);

            List<MemoryFragmentSnapshot> store = new List<MemoryFragmentSnapshot>
            {
                Frag("evt-a", 1000000, raidExtraction.tags.ToArray(), raidExtraction.keywords.ToArray(),
                    raidExtraction.importance, raidExtraction.fragmentText, sourceEventId: "evt-a"),
                Frag("evt-b", 1000000, Tags("work"), Tags("fields", "rice"), 0.4f, "A long sowing day."),
                Frag("evt-c", 1000000, Tags("illness"), Tags("flu", "bedrest"), 0.4f, "A bad flu week."),
                Frag("evt-d", 1000000, Tags("family"), Tags("mother", "letter"), 0.4f, "A letter from mother.")
            };

            MemoryExtractionResult laterEvent = MemoryExtraction.Extract(new MemoryExtractionInput
            {
                povName = "Daisy",
                otherName = "Yorick",
                colorCue = "danger",
                solo = false,
                rawText = "More raiders on the ridge."
            }, policy);
            MemoryRecallQuery query = new MemoryRecallQuery
            {
                tags = laterEvent.tags,
                keywords = laterEvent.keywords,
                currentEventId = "evt-now",
                currentTick = 1120000,
                seed = 7
            };
            MemoryRecallResult result = MemoryRecallSelector.Recall(query, store, policy);
            AssertEqual("a similar later event recalls the earlier fragment", 1, result.picks.Count);
            AssertEqual("recalled fragment id", "evt-a", result.picks[0].memoryId);
            AssertTrue("rendered context uses the frozen excerpt",
                result.memoryContext.Contains("Raiders came with rifles."));
        }

        // ---------------------------------------------------------------------------------------------
        // Eviction
        // ---------------------------------------------------------------------------------------------

        private static void TestEvictionStaleRule()
        {
            MemoryPolicySnapshot policy = MemoryPolicySnapshot.CreateDefault();

            List<MemoryFragmentSnapshot> store = new List<MemoryFragmentSnapshot>
            {
                Frag("stale", 0, Tags("work"), Tags("fields"), 0.5f, "old"),
                Frag("fresh", 7000001, Tags("work"), Tags("fields"), 0.5f, "new")
            };
            List<string> evict = MemoryEvictionPlanner.Plan(store, 7200001, policy);
            AssertSequence("stale ordinary memory fades after two years", new[] { "stale" }, evict);

            List<string> boundary = MemoryEvictionPlanner.Plan(store, 7200000, policy);
            AssertEqual("exactly at the stale window is not stale", 0, boundary.Count);

            List<MemoryFragmentSnapshot> coreStore = new List<MemoryFragmentSnapshot>
            {
                Frag("core", 0, Tags("death"), Tags("yorick"), 0.9f, "core memory")
            };
            AssertEqual("core memories are exempt from the stale rule", 0,
                MemoryEvictionPlanner.Plan(coreStore, 50000000, policy).Count);

            List<MemoryFragmentSnapshot> recalled = new List<MemoryFragmentSnapshot>
            {
                Frag("recalled", 0, Tags("work"), Tags("fields"), 0.5f, "kept alive", lastRecalledTick: 7000000)
            };
            AssertEqual("a recent recall refreshes a fragment past the stale rule", 0,
                MemoryEvictionPlanner.Plan(recalled, 7200001, policy).Count);

            recalled[0].lastRecalledTick = 1;
            AssertSequence("an ancient recall no longer protects", new[] { "recalled" },
                MemoryEvictionPlanner.Plan(recalled, 7200002, policy));
        }

        private static void TestEvictionCapsAndTieBreaks()
        {
            MemoryPolicySnapshot policy = MemoryPolicySnapshot.CreateDefault();
            policy.staleEvictTicks = int.MaxValue; // isolate the cap rules from the stale rule

            // Per-pawn cap with equal retention: older creation loses; then lower recallCount; then id.
            policy.maxFragmentsPerPawn = 3;
            List<MemoryFragmentSnapshot> tieStore = new List<MemoryFragmentSnapshot>
            {
                Frag("m1", 0, Tags("work"), Tags("fields"), 0.5f, "t", recallCount: 0),
                Frag("m2", 0, Tags("work"), Tags("fields"), 0.5f, "t", recallCount: 1),
                Frag("m3", 0, Tags("work"), Tags("fields"), 0.5f, "t", recallCount: 2),
                Frag("m4", 0, Tags("work"), Tags("fields"), 0.5f, "t", recallCount: 3),
                Frag("m5", 0, Tags("work"), Tags("fields"), 0.5f, "t", recallCount: 4)
            };
            AssertSequence("cap evicts least-recalled first on tied retention", new[] { "m1", "m2" },
                MemoryEvictionPlanner.Plan(tieStore, 1800000, policy));

            List<MemoryFragmentSnapshot> idStore = new List<MemoryFragmentSnapshot>
            {
                Frag("md", 0, Tags("work"), Tags("fields"), 0.5f, "t"),
                Frag("mc", 0, Tags("work"), Tags("fields"), 0.5f, "t"),
                Frag("mb", 0, Tags("work"), Tags("fields"), 0.5f, "t"),
                Frag("ma", 0, Tags("work"), Tags("fields"), 0.5f, "t"),
                Frag("me", 0, Tags("work"), Tags("fields"), 0.5f, "t")
            };
            AssertSequence("fully tied eviction falls back to memory id order", new[] { "ma", "mb" },
                MemoryEvictionPlanner.Plan(idStore, 1800000, policy));

            // Retention ordering: importance x decay, not insertion order.
            policy.maxFragmentsPerPawn = 2;
            List<MemoryFragmentSnapshot> retentionStore = new List<MemoryFragmentSnapshot>
            {
                Frag("weak-fresh", 3600000, Tags("work"), Tags("fields"), 0.1f, "t"),
                Frag("strong-old", 0, Tags("work"), Tags("fields"), 0.7f, "t"),
                Frag("mid-fresh", 3600000, Tags("work"), Tags("fields"), 0.2f, "t"),
                Frag("core-fresh", 3600000, Tags("work"), Tags("fields"), 0.9f, "t")
            };
            AssertSequence("lowest retention evaporates first", new[] { "weak-fresh", "strong-old" },
                MemoryEvictionPlanner.Plan(retentionStore, 3600000, policy));

            // Core cap: overflow evicts the core fragment with the oldest freshness.
            policy.maxFragmentsPerPawn = 60;
            policy.maxCoreFragmentsPerPawn = 2;
            List<MemoryFragmentSnapshot> coreStore = new List<MemoryFragmentSnapshot>
            {
                Frag("c1", 0, Tags("death"), Tags("yorick"), 0.9f, "t", lastRecalledTick: 100),
                Frag("c2", 0, Tags("death"), Tags("yorick"), 0.9f, "t", lastRecalledTick: 200),
                Frag("c3", 0, Tags("death"), Tags("yorick"), 0.9f, "t", lastRecalledTick: 300),
                Frag("c4", 0, Tags("death"), Tags("yorick"), 0.9f, "t", lastRecalledTick: 400)
            };
            AssertSequence("core cap evicts oldest freshness", new[] { "c1", "c2" },
                MemoryEvictionPlanner.Plan(coreStore, 1000000, policy));

            // The per-pawn cap never evicts core fragments to make room.
            policy.maxFragmentsPerPawn = 2;
            policy.maxCoreFragmentsPerPawn = 5;
            List<MemoryFragmentSnapshot> mixed = new List<MemoryFragmentSnapshot>
            {
                Frag("core-1", 1000000, Tags("death"), Tags("yorick"), 0.9f, "t"),
                Frag("core-2", 1000000, Tags("death"), Tags("yorick"), 0.9f, "t"),
                Frag("plain-1", 1000000, Tags("work"), Tags("fields"), 0.5f, "t"),
                Frag("plain-2", 1000000, Tags("work"), Tags("fields"), 0.4f, "t")
            };
            AssertSequence("per-pawn cap sheds only non-core fragments", new[] { "plain-2", "plain-1" },
                MemoryEvictionPlanner.Plan(mixed, 1000000, policy));

            // Null and blank-id rows are unplannable and simply skipped.
            List<MemoryFragmentSnapshot> malformed = new List<MemoryFragmentSnapshot>
            {
                null,
                Frag(" ", 0, Tags("work"), Tags("fields"), 0.0f, "t")
            };
            AssertEqual("malformed rows produce no eviction ids", 0,
                MemoryEvictionPlanner.Plan(malformed, 50000000, policy).Count);
        }

        private static void TestEvictionGlobalCapAndNoMutation()
        {
            MemoryPolicySnapshot policy = MemoryPolicySnapshot.CreateDefault();
            policy.staleEvictTicks = int.MaxValue;
            policy.maxTotalFragments = 3;

            // Core status does not protect against the colony-wide cap: retention is all that counts.
            List<MemoryFragmentSnapshot> all = new List<MemoryFragmentSnapshot>
            {
                Frag("g1", 0, Tags("work"), Tags("fields"), 0.1f, "t", pawnId: "pawnA"),
                Frag("g2", 0, Tags("death"), Tags("yorick"), 0.8f, "t", pawnId: "pawnB"),
                Frag("g3", 3600000, Tags("work"), Tags("fields"), 0.2f, "t", pawnId: "pawnA"),
                Frag("g4", 3600000, Tags("work"), Tags("fields"), 0.9f, "t", pawnId: "pawnB"),
                Frag("g5", 3600000, Tags("work"), Tags("fields"), 0.5f, "t", pawnId: "pawnA")
            };
            List<string> evict = MemoryEvictionPlanner.PlanGlobalCap(all, 7200000, policy);
            AssertEqual("global cap removes the overflow count", 2, evict.Count);
            AssertSequence("global cap takes the lowest retention colony-wide, core included",
                new[] { "g1", "g2" }, evict);

            AssertEqual("under the global cap nothing is planned", 0,
                MemoryEvictionPlanner.PlanGlobalCap(all.GetRange(0, 3), 7200000, policy).Count);

            // Planning never mutates its inputs.
            List<MemoryFragmentSnapshot> frozen = new List<MemoryFragmentSnapshot>
            {
                Frag("x1", 0, Tags("work"), Tags("fields"), 0.1f, "t", recallCount: 2),
                Frag("x2", 0, Tags("work"), Tags("fields"), 0.9f, "t", recallCount: 1),
                Frag("x3", 100, Tags("work"), Tags("fields"), 0.2f, "t", recallCount: 0)
            };
            policy.maxFragmentsPerPawn = 1;
            List<string> plan = MemoryEvictionPlanner.Plan(frozen, 3600000, policy);
            AssertTrue("plan produced evictions", plan.Count > 0);
            AssertEqual("input list length unchanged", 3, frozen.Count);
            AssertEqual("input order unchanged 0", "x1", frozen[0].memoryId);
            AssertEqual("input order unchanged 1", "x2", frozen[1].memoryId);
            AssertEqual("input order unchanged 2", "x3", frozen[2].memoryId);
            AssertEqual("input recall bookkeeping unchanged", 2, frozen[0].recallCount);
            AssertEqual("input recall tick unchanged", 0, frozen[0].lastRecalledTick);

            MemoryPolicySnapshot defaultPolicy = MemoryPolicySnapshot.CreateDefault();
            AssertEqual("a null store plans nothing", 0,
                MemoryEvictionPlanner.Plan(null, 3600000, defaultPolicy).Count);
            AssertEqual("a null global store plans nothing", 0,
                MemoryEvictionPlanner.PlanGlobalCap(null, 3600000, defaultPolicy).Count);
        }

        // ---------------------------------------------------------------------------------------------
        // Prompt composer
        // ---------------------------------------------------------------------------------------------

        private static void TestPromptCompose()
        {
            AssertEqual("stable source token", "MemoryContext", MemoryContextPrompt.Source);
            AssertEqual("empty context yields no field", string.Empty,
                MemoryContextPrompt.Compose(string.Empty, "instr"));
            AssertEqual("whitespace context yields no field", string.Empty,
                MemoryContextPrompt.Compose("   ", "instr"));
            AssertEqual("missing instruction passes memories through", "lines",
                MemoryContextPrompt.Compose("lines", null));
            AssertEqual("blank instruction passes memories through", "lines",
                MemoryContextPrompt.Compose("lines", "  "));
            AssertEqual("instruction prefixes memories on their own line", "instr\nlines",
                MemoryContextPrompt.Compose("lines", "instr"));
            AssertEqual("both sides are trimmed", "instr\nlines",
                MemoryContextPrompt.Compose("  lines  ", "  instr  "));
        }

        // ---------------------------------------------------------------------------------------------
        // Lore seeds (LORE_MEMORY_SEED_PLAN L2)
        // ---------------------------------------------------------------------------------------------

        private static void TestLoreSeedProvenanceAndTokens()
        {
            AssertEqual("initial sentinel shape", "loreseed:LoreSeed_X",
                LoreSeedProvenance.InitialSourceEventId("LoreSeed_X"));
            AssertEqual("progression sentinel shape", "loreseed-progression:evt-1:LoreSeed_X",
                LoreSeedProvenance.ProgressionSourceEventId("evt-1", "LoreSeed_X"));
            AssertTrue("initial sentinel detected",
                LoreSeedProvenance.IsLoreSourceEventId("loreseed:LoreSeed_X"));
            AssertTrue("progression sentinel detected",
                LoreSeedProvenance.IsLoreSourceEventId("loreseed-progression:evt-1:LoreSeed_X"));
            AssertTrue("guid event ids are never lore",
                !LoreSeedProvenance.IsLoreSourceEventId("3f9c2d5a41f04e28b7a9d2c8e5f01234"));

            AssertTrue("usage tokens", LoreSeedTokens.IsKnownUsage("initial")
                && LoreSeedTokens.IsKnownUsage("progression") && LoreSeedTokens.IsKnownUsage("both")
                && !LoreSeedTokens.IsKnownUsage("Initial") && !LoreSeedTokens.IsKnownUsage(""));
            AssertTrue("tier tokens", LoreSeedTokens.IsKnownTier("ordinary")
                && LoreSeedTokens.IsKnownTier("core") && !LoreSeedTokens.IsKnownTier("Core"));

            MemoryPolicySnapshot policy = MemoryPolicySnapshot.CreateDefault();
            AssertTrue("default loreSeedsEnabled", policy.loreSeedsEnabled);
            AssertEqual("default maxInitialLoreSeedsPerPawn", 4, policy.maxInitialLoreSeedsPerPawn);
            AssertEqual("default minSpecificInitialLoreSeedsPerPawn", 1, policy.minSpecificInitialLoreSeedsPerPawn);
            AssertNear("default loreSeedOrdinaryImportance", 0.35f, policy.loreSeedOrdinaryImportance, 0.0001f);
            AssertNear("default loreSeedCoreImportance", 0.85f, policy.loreSeedCoreImportance, 0.0001f);
            AssertEqual("default loreSeedNarrativeAgeOffsetTicks", 7200000, policy.loreSeedNarrativeAgeOffsetTicks);
            AssertEqual("default maxCoreLoreSeedsPerPawnLifetime", 2, policy.maxCoreLoreSeedsPerPawnLifetime);
            AssertEqual("default coreLoreRecallCooldownTicks", 1200000, policy.coreLoreRecallCooldownTicks);
            AssertEqual("default maxProgressionLoreSeedsPerPawnLifetime", 4, policy.maxProgressionLoreSeedsPerPawnLifetime);

            LoreSeedPolicy lore = LoreSeedPolicy.FromMemoryPolicy(policy, true);
            AssertTrue("policy copy enabled", lore.enabled);
            AssertNear("core importance clears the core threshold",
                Math.Max(0.85f, policy.coreImportanceThreshold), lore.coreImportance, 0.0001f);
            LoreSeedPolicy disabled = LoreSeedPolicy.FromMemoryPolicy(policy, false);
            AssertTrue("setting=false disables the copied policy", !disabled.enabled);
        }

        private static void TestLoreSeedEligibility()
        {
            LoreSeedPawnFacts facts = LoreFacts(
                cats: Tags("Tribal", "Offworld"),
                backstories: Tags("TribalChild4", "Hunter12"),
                xeno: "Hussar",
                hediffs: Tags("MechlinkImplant"));

            AssertTrue("generic seed always eligible",
                LoreSeedPlanner.IsEligible(LoreSeed("g"), facts));
            AssertTrue("matching category eligible",
                LoreSeedPlanner.IsEligible(LoreSeed("c", cats: Tags("Tribal")), facts));
            AssertTrue("non-matching category ineligible",
                !LoreSeedPlanner.IsEligible(LoreSeed("c2", cats: Tags("Imperial")), facts));
            AssertTrue("exclusion category blocks",
                !LoreSeedPlanner.IsEligible(LoreSeed("x", excludeCats: Tags("Offworld")), facts));
            AssertTrue("exact backstory eligible",
                LoreSeedPlanner.IsEligible(LoreSeed("b", backstories: Tags("Hunter12")), facts));
            AssertTrue("missing exact backstory ineligible",
                !LoreSeedPlanner.IsEligible(LoreSeed("b2", backstories: Tags("Urchin3")), facts));
            AssertTrue("exclude exact backstory blocks",
                !LoreSeedPlanner.IsEligible(LoreSeed("b3", excludeBackstories: Tags("TribalChild4")), facts));
            AssertTrue("xenotype match eligible",
                LoreSeedPlanner.IsEligible(LoreSeed("xe", xenos: Tags("Hussar")), facts));
            AssertTrue("xenotype mismatch ineligible",
                !LoreSeedPlanner.IsEligible(LoreSeed("xe2", xenos: Tags("Sanguophage")), facts));
            AssertTrue("hediff match eligible",
                LoreSeedPlanner.IsEligible(LoreSeed("h", hediffs: Tags("MechlinkImplant")), facts));
            AssertTrue("all populated positive types are required together",
                !LoreSeedPlanner.IsEligible(
                    LoreSeed("mix", cats: Tags("Tribal"), xenos: Tags("Sanguophage")), facts));
            AssertTrue("progression usage never eligible for the initial plan",
                !LoreSeedPlanner.IsEligible(LoreSeed("p", usage: "progression"), facts));
            AssertTrue("zero weight ineligible",
                !LoreSeedPlanner.IsEligible(LoreSeed("w", weight: 0f), facts));

            LoreSeedPawnFacts used = LoreFacts();
            used.initialTargetDefNames.Add("g");
            AssertTrue("a name already in the roster is excluded",
                !LoreSeedPlanner.IsEligible(LoreSeed("g"), used));
            used.progressionDefNamesEverDeposited.Add("p2");
            AssertTrue("a name in the progression history is excluded",
                !LoreSeedPlanner.IsEligible(LoreSeed("p2"), used));

            AssertTrue("generic is not specific", !LoreSeedPlanner.IsSpecific(LoreSeed("g")));
            AssertTrue("category constraint is specific",
                LoreSeedPlanner.IsSpecific(LoreSeed("c", cats: Tags("Tribal"))));
            AssertTrue("hediff constraint is specific",
                LoreSeedPlanner.IsSpecific(LoreSeed("h", hediffs: Tags("MechlinkImplant"))));
        }

        private static void TestLoreSeedPlanInitialDeterminismAndCaps()
        {
            LoreSeedPolicy policy = new LoreSeedPolicy();
            LoreSeedPawnFacts facts = LoreFacts(backstories: Tags("Hunter12"));
            List<LoreSeedCandidate> catalog = new List<LoreSeedCandidate>
            {
                LoreSeed("g1"), LoreSeed("g2"), LoreSeed("g3"),
                LoreSeed("g4"), LoreSeed("g5"), LoreSeed("g6")
            };

            List<LoreSeedPick> first = LoreSeedPlanner.PlanInitial(catalog, facts, policy, 42);
            List<LoreSeedPick> second = LoreSeedPlanner.PlanInitial(catalog, facts, policy, 42);
            AssertEqual("roster fills to the initial maximum", 4, first.Count);
            AssertEqual("same seed same roster size", first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                AssertEqual("same seed same roster order " + i,
                    first[i].seedDefName, second[i].seedDefName);
            }

            LoreSeedPawnFacts partial = LoreFacts();
            partial.initialTargetDefNames.Add("g1");
            partial.initialTargetDefNames.Add("g2");
            partial.initialTargetDefNames.Add("g3");
            List<LoreSeedPick> remaining = LoreSeedPlanner.PlanInitial(catalog, partial, policy, 42);
            AssertEqual("existing roster consumes lifetime capacity", 1, remaining.Count);
            AssertTrue("remaining pick avoids used names",
                remaining[0].seedDefName != "g1" && remaining[0].seedDefName != "g2"
                && remaining[0].seedDefName != "g3");

            // Core lifetime cap: core candidates beyond capacity are discarded without
            // consuming a roster slot.
            List<LoreSeedCandidate> coreHeavy = new List<LoreSeedCandidate>
            {
                LoreSeed("c1", tier: "core", backstories: Tags("Hunter12")),
                LoreSeed("c2", tier: "core", backstories: Tags("Hunter12")),
                LoreSeed("c3", tier: "core", backstories: Tags("Hunter12")),
                LoreSeed("o1"), LoreSeed("o2"), LoreSeed("o3")
            };
            List<LoreSeedPick> capped = LoreSeedPlanner.PlanInitial(coreHeavy, facts, policy, 7);
            AssertEqual("roster still fills all four slots", 4, capped.Count);
            AssertTrue("core picks never exceed the lifetime cap of two",
                CountCore(capped) >= 1 && CountCore(capped) <= 2);

            LoreSeedPawnFacts coreSpent = LoreFacts(backstories: Tags("Hunter12"));
            coreSpent.coreDefNamesEverDeposited.Add("old1");
            coreSpent.coreDefNamesEverDeposited.Add("old2");
            List<LoreSeedPick> noCore = LoreSeedPlanner.PlanInitial(coreHeavy, coreSpent, policy, 7);
            AssertEqual("spent core history admits zero new core seeds", 0, CountCore(noCore));
            AssertEqual("ordinary seeds still fill what they can", 3, noCore.Count);

            List<LoreSeedPick> disabled = LoreSeedPlanner.PlanInitial(
                catalog, facts, new LoreSeedPolicy { enabled = false }, 42);
            AssertEqual("disabled policy plans nothing", 0, disabled.Count);
        }

        private static void TestLoreSeedSpecificReservationAndMutex()
        {
            LoreSeedPolicy policy = new LoreSeedPolicy();
            LoreSeedPawnFacts facts = LoreFacts(
                cats: Tags("Tribal"), backstories: Tags("Hunter12"));

            List<LoreSeedCandidate> catalog = new List<LoreSeedCandidate>
            {
                LoreSeed("g1"), LoreSeed("g2"), LoreSeed("g3"),
                LoreSeed("s1", cats: Tags("Tribal")),
                LoreSeed("c1", tier: "core", backstories: Tags("Hunter12"))
            };

            List<LoreSeedPick> picks = LoreSeedPlanner.PlanInitial(catalog, facts, policy, 11);
            AssertEqual("reserved slot is drawn first and is core-specific", "c1",
                picks[0].seedDefName);
            AssertTrue("reserved pick is flagged specific", picks[0].specific && picks[0].core);

            LoreSeedPawnFacts coreSpent = LoreFacts(
                cats: Tags("Tribal"), backstories: Tags("Hunter12"));
            coreSpent.coreDefNamesEverDeposited.Add("old1");
            coreSpent.coreDefNamesEverDeposited.Add("old2");
            List<LoreSeedPick> ordinaryReserved = LoreSeedPlanner.PlanInitial(catalog, coreSpent, policy, 11);
            AssertEqual("without core capacity the reserved slot is the ordinary specific seed",
                "s1", ordinaryReserved[0].seedDefName);
            AssertTrue("no core pick without capacity", CountCore(ordinaryReserved) == 0);

            List<LoreSeedCandidate> genericOnly = new List<LoreSeedCandidate>
            {
                LoreSeed("g1"), LoreSeed("g2")
            };
            List<LoreSeedPick> unfilled = LoreSeedPlanner.PlanInitial(genericOnly, facts, policy, 11);
            AssertEqual("no specific candidate fills the roster with generics anyway", 2, unfilled.Count);
            AssertTrue("no pick is falsely flagged specific",
                !unfilled[0].specific && !unfilled[1].specific);

            // Mutex applies only inside this one construction: picking one member of the group
            // removes its siblings from the pool.
            LoreSeedPolicy two = new LoreSeedPolicy { maxInitialSeeds = 3, minSpecificInitialSeeds = 0 };
            List<LoreSeedCandidate> mutexed = new List<LoreSeedCandidate>
            {
                LoreSeed("m1", mutex: "origin"), LoreSeed("m2", mutex: "origin"), LoreSeed("o1")
            };
            List<LoreSeedPick> mutexPicks = LoreSeedPlanner.PlanInitial(mutexed, LoreFacts(), two, 5);
            AssertEqual("mutex group yields one member plus the outsider", 2, mutexPicks.Count);
            bool m1Picked = ContainsPick(mutexPicks, "m1");
            bool m2Picked = ContainsPick(mutexPicks, "m2");
            AssertTrue("exactly one mutex sibling is picked", m1Picked != m2Picked);
            AssertTrue("the outsider always fits", ContainsPick(mutexPicks, "o1"));
        }

        private static void TestLoreSeedNarrativeOffsetClamp()
        {
            AssertEqual("offset clamps to a young pawn's biological age", 100000,
                LoreSeedPlanner.ClampNarrativeOffset(7200000, 100000L));
            AssertEqual("offset passes through under an old pawn's age", 7200000,
                LoreSeedPlanner.ClampNarrativeOffset(7200000, 100000000L));
            AssertEqual("negative policy offset clamps to zero", 0,
                LoreSeedPlanner.ClampNarrativeOffset(-5, 100000L));
            AssertEqual("negative biological age clamps to zero", 0,
                LoreSeedPlanner.ClampNarrativeOffset(7200000, -3L));
        }

        /// <summary>
        /// LORE_MEMORY_SEED_PLAN §7.2 (L5): PlanProgression matches the exact registered event
        /// token only, returns at most one deterministic pick, excludes every used Def name, and
        /// enforces the progression and core lifetime capacities.
        /// </summary>
        private static void TestLoreSeedPlanProgression()
        {
            LoreSeedPolicy policy = new LoreSeedPolicy();
            LoreSeedPawnFacts facts = LoreFacts(cats: Tags("Civil"));
            List<LoreSeedCandidate> catalog = new List<LoreSeedCandidate>
            {
                ProgressionSeed("p1", "XenotypeChanged"),
                ProgressionSeed("p2", "XenotypeChanged"),
                ProgressionSeed("q1", "PsylinkLevel"),
                LoreSeed("g1") // initial usage: never a progression pick
            };

            LoreSeedProgressionFacts xenotype = new LoreSeedProgressionFacts
            {
                eventId = "evt-1",
                eventDefName = "XenotypeChanged"
            };
            LoreSeedPick pick = LoreSeedPlanner.PlanProgression(catalog, facts, xenotype, policy, 9);
            AssertTrue("a matching token yields one pick",
                pick != null && (pick.seedDefName == "p1" || pick.seedDefName == "p2"));
            LoreSeedPick again = LoreSeedPlanner.PlanProgression(catalog, facts, xenotype, policy, 9);
            AssertEqual("same seed same pick", pick.seedDefName, again.seedDefName);

            AssertTrue("a non-matching token yields nothing",
                LoreSeedPlanner.PlanProgression(catalog, facts, new LoreSeedProgressionFacts
                {
                    eventId = "evt-2",
                    eventDefName = "RoyalTitleGained"
                }, policy, 9) == null);
            AssertTrue("initial-usage seeds are never progression-eligible",
                !LoreSeedPlanner.IsEligibleForProgression(LoreSeed("g1"), facts, "XenotypeChanged"));

            // Exact-Def uniqueness across the roster and both histories (§6).
            LoreSeedPawnFacts used = LoreFacts(cats: Tags("Civil"));
            used.initialTargetDefNames.Add("p1");
            used.progressionDefNamesEverDeposited.Add("p2");
            AssertTrue("used names are excluded, exhausting the pool",
                LoreSeedPlanner.PlanProgression(catalog, used, xenotype, policy, 9) == null);

            // Progression lifetime cap (default 4).
            LoreSeedPawnFacts capped = LoreFacts(cats: Tags("Civil"));
            capped.progressionDefNamesEverDeposited.AddRange(
                new[] { "old1", "old2", "old3", "old4" });
            AssertTrue("the progression lifetime cap blocks a fifth seed",
                LoreSeedPlanner.PlanProgression(catalog, capped, xenotype, policy, 9) == null);

            // Core capacity: a core progression candidate is skipped once the lifetime is spent.
            List<LoreSeedCandidate> coreOnly = new List<LoreSeedCandidate>
            {
                ProgressionSeed("c1", "XenotypeChanged", tier: "core", xenos: Tags("Hussar"))
            };
            LoreSeedPawnFacts hussar = LoreFacts(cats: Tags("Civil"), xeno: "Hussar");
            LoreSeedPick corePick = LoreSeedPlanner.PlanProgression(coreOnly, hussar, xenotype, policy, 9);
            AssertTrue("a core progression pick is possible with capacity",
                corePick != null && corePick.core);
            LoreSeedPawnFacts coreSpent = LoreFacts(cats: Tags("Civil"), xeno: "Hussar");
            coreSpent.coreDefNamesEverDeposited.Add("old1");
            coreSpent.coreDefNamesEverDeposited.Add("old2");
            AssertTrue("spent core lifetime blocks a core progression pick",
                LoreSeedPlanner.PlanProgression(coreOnly, coreSpent, xenotype, policy, 9) == null);

            // Pawn constraints still apply to progression candidates.
            AssertTrue("pawn constraints gate progression eligibility",
                !LoreSeedPlanner.IsEligibleForProgression(
                    ProgressionSeed("x1", "XenotypeChanged", xenos: Tags("Sanguophage")),
                    facts, "XenotypeChanged"));

            AssertTrue("disabled policy plans nothing",
                LoreSeedPlanner.PlanProgression(catalog, facts, xenotype,
                    new LoreSeedPolicy { enabled = false }, 9) == null);

            // Null/empty guards never throw and never pick.
            AssertTrue("null candidates yield nothing",
                LoreSeedPlanner.PlanProgression(null, facts, xenotype, policy, 9) == null);
            AssertTrue("null facts yield nothing",
                LoreSeedPlanner.PlanProgression(catalog, null, xenotype, policy, 9) == null);
            AssertTrue("null progression facts yield nothing",
                LoreSeedPlanner.PlanProgression(catalog, facts, null, policy, 9) == null);
            AssertTrue("blank event token yields nothing",
                LoreSeedPlanner.PlanProgression(catalog, facts,
                    new LoreSeedProgressionFacts { eventId = "evt-3" }, policy, 9) == null);
            AssertTrue("a zero progression lifetime plans nothing",
                LoreSeedPlanner.PlanProgression(catalog, facts, xenotype,
                    new LoreSeedPolicy { maxProgressionSeedsLifetime = 0 }, 9) == null);

            // §7.2: mutexGroup has NO cross-event semantics — after one sibling is deposited,
            // the other stays plantable for a later event; only exact-Def reuse is blocked.
            List<LoreSeedCandidate> mutexed = new List<LoreSeedCandidate>
            {
                ProgressionSeed("m1", "XenotypeChanged"),
                ProgressionSeed("m2", "XenotypeChanged")
            };
            mutexed[0].mutexGroup = "prog_pair";
            mutexed[1].mutexGroup = "prog_pair";
            LoreSeedPick firstEvent = LoreSeedPlanner.PlanProgression(mutexed, facts, xenotype, policy, 21);
            AssertTrue("first event picks one mutex sibling", firstEvent != null);
            LoreSeedPawnFacts afterFirst = LoreFacts(cats: Tags("Civil"));
            afterFirst.progressionDefNamesEverDeposited.Add(firstEvent.seedDefName);
            LoreSeedPick secondEvent = LoreSeedPlanner.PlanProgression(mutexed, afterFirst,
                new LoreSeedProgressionFacts { eventId = "evt-4", eventDefName = "XenotypeChanged" },
                policy, 22);
            AssertTrue("the mutex sibling stays plantable for a later event",
                secondEvent != null && secondEvent.seedDefName != firstEvent.seedDefName);

            // 'both' usage: eligible for initial AND progression, but one lifetime deposit total.
            LoreSeedCandidate dual = LoreSeed("dual", usage: "both");
            dual.progressionEventDefNames.Add("XenotypeChanged");
            AssertTrue("'both' usage is initial-eligible",
                LoreSeedPlanner.IsEligible(dual, facts));
            AssertTrue("'both' usage is progression-eligible",
                LoreSeedPlanner.IsEligibleForProgression(dual, facts, "XenotypeChanged"));
            LoreSeedPawnFacts dualUsed = LoreFacts(cats: Tags("Civil"));
            dualUsed.initialTargetDefNames.Add("dual");
            AssertTrue("a 'both' seed used as an initial target is progression-excluded",
                !LoreSeedPlanner.IsEligibleForProgression(dual, dualUsed, "XenotypeChanged"));
        }

        private static LoreSeedCandidate ProgressionSeed(string name, string eventToken,
            string tier = "ordinary", string[] xenos = null)
        {
            LoreSeedCandidate candidate = LoreSeed(name, usage: "progression", tier: tier, xenos: xenos);
            candidate.progressionEventDefNames.Add(eventToken);
            return candidate;
        }

        private static void TestCoreLoreCooldownGate()
        {
            MemoryPolicySnapshot policy = RecallPolicy();
            List<MemoryFragmentSnapshot> store = new List<MemoryFragmentSnapshot>
            {
                Frag("core-seed", 0, Tags("combat", "danger"), Tags("yorick", "rifle", "fire"),
                    0.85f, "The elders' machine story."),
                Frag("f1", 0, Tags("work"), Tags("fields"), 0.4f, "Working the fields."),
                Frag("f2", 0, Tags("illness"), Tags("flu"), 0.4f, "A bad flu week."),
                Frag("f3", 0, Tags("family"), Tags("mother"), 0.4f, "A letter from mother.")
            };
            store[0].loreSeedDefName = "LoreSeed_Test";

            // Never surfaced (lastRecalledTick == createdTick): immediately eligible.
            MemoryRecallResult fresh = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 1000000), store, policy);
            AssertEqual("an unsurfaced core seed is immediately eligible", 1, fresh.picks.Count);

            // Surfaced 500k ticks ago: past the ordinary 300k cooldown but inside the 1.2M core
            // window -> hard-ineligible, not merely quartered.
            store[0].lastRecalledTick = 500000;
            MemoryRecallResult gated = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 1000000), store, policy);
            AssertEqual("a surfaced core seed is hard-gated for 20 days", 0, gated.picks.Count);

            // An ordinary lore row with the same recall pattern stays eligible.
            store[0].importance = 0.35f;
            MemoryRecallResult ordinary = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 1000000), store, policy);
            AssertEqual("ordinary lore keeps the normal cooldown behavior", 1, ordinary.picks.Count);

            // At exactly the core-cooldown boundary the seed is merely eligible again.
            store[0].importance = 0.85f;
            MemoryRecallResult expired = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 1700000), store, policy);
            AssertEqual("the core cooldown boundary restores eligibility", 1, expired.picks.Count);

            // A high-importance LIVED memory (no lore provenance) never hits the core-lore gate.
            store[0].loreSeedDefName = string.Empty;
            MemoryRecallResult lived = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 1000000), store, policy);
            AssertEqual("core lived memories are unaffected by the lore gate", 1, lived.picks.Count);
        }

        private static void TestEvictionLoreSuppression()
        {
            MemoryPolicySnapshot policy = MemoryPolicySnapshot.CreateDefault();
            policy.maxFragmentsPerPawn = 2;
            const int now = 10000000;

            // A stale ordinary lore row plus two fresh lived rows.
            List<MemoryFragmentSnapshot> fragments = new List<MemoryFragmentSnapshot>
            {
                Frag("lore-stale", 100, Tags("dread"), Tags("monolith"), 0.35f, "Old lore."),
                Frag("lived-1", now - 1000, Tags("work"), Tags("fields"), 0.5f, "Fresh one."),
                Frag("lived-2", now - 2000, Tags("joy"), Tags("party"), 0.5f, "Fresh two.")
            };
            fragments[0].loreSeedDefName = "LoreSeed_Test";

            List<string> suppressed = MemoryEvictionPlanner.Plan(fragments, now, policy, suppressLore: true);
            AssertEqual("suppressed lore is neither counted nor evicted", 0, suppressed.Count);

            List<string> active = MemoryEvictionPlanner.Plan(fragments, now, policy);
            AssertContains("enabled lore is subject to the normal stale rule", active, "lore-stale");

            policy.maxTotalFragments = 2;
            List<string> globalSuppressed = MemoryEvictionPlanner.PlanGlobalCap(
                fragments, now, policy, suppressLore: true);
            AssertEqual("suppressed lore does not count toward the global cap", 0, globalSuppressed.Count);
            List<string> globalActive = MemoryEvictionPlanner.PlanGlobalCap(fragments, now, policy);
            AssertEqual("enabled lore counts toward the global cap", 1, globalActive.Count);
        }

        /// <summary>
        /// LORE_MEMORY_SEED_PLAN §5/§10: authored seed keywords normalize through the exact
        /// identity tokenizer used for deposit/query values — short tokens and stopword-lookalike
        /// Def names survive, prose noise rules do not apply, duplicates drop, the cap holds.
        /// </summary>
        private static void TestAuthoredKeywordNormalization()
        {
            List<string> normalized = MemoryExtraction.NormalizeAuthoredKeywords(
                new List<string> { "MechanoidCluster", "Mechanoid", "mechanoid", "The", "Ur" }, 8);
            AssertSequence("identity tokenization keeps short and stopword-like tokens",
                new[] { "mechanoidcluster", "mechanoid", "the", "ur" }, normalized);

            List<string> capped = MemoryExtraction.NormalizeAuthoredKeywords(
                new List<string> { "Alpha", "Beta", "Gamma" }, 2);
            AssertEqual("keyword cap holds", 2, capped.Count);

            List<string> split = MemoryExtraction.NormalizeAuthoredKeywords(
                new List<string> { "Psychic-Drone" }, 8);
            AssertSequence("non-alphanumerics split into stable tokens",
                new[] { "psychic", "drone" }, split);

            AssertEqual("null input yields an empty list", 0,
                MemoryExtraction.NormalizeAuthoredKeywords(null, 8).Count);
        }

        // ---------------------------------------------------------------------------------------------
        // Lore catalog QA (LORE_MEMORY_SEED_PLAN §11 pass 4 / §14.2)
        // ---------------------------------------------------------------------------------------------

        // The frozen matcher vocabulary from the pass-1 audit. Every catalog keyword must be one
        // of these audited stable identifiers; growing this list requires re-auditing the query
        // path (§10) and updating the catalog header comment.
        private static readonly string[] AuditedKeywordVocabulary =
        {
            "RaidEnemy", "Infestation",
            "Mechanoid", "Insect", "Empire", "Pirate",
            "Plague", "Flu", "Malaria", "LuciferiumHigh", "PsychicAmplifier", "MechlinkImplant",
            "PsychicDrone", "PsychicSoothe", "ToxicFallout", "SolarFlare", "Eclipse",
            "ToxicFalloutActive", "SolarFlareActive", "ThrumboVisit"
        };

        private static readonly string[] KnownMutexGroups =
        {
            "distance", "cryptosleep", "glitterworld", "homeworld", "archotech", "psychic",
            "mechanoid_origin", "insectoid", "xeno_identity", "empire", "void", "legends",
            "vatgrown", "outlaw", "tribal_root", "rim_life", "weather_lore", "trade",
            "prog_psylink", "prog_xenotype", "prog_genes", "prog_mechlink", "prog_title"
        };

        // The audited registered progression event tokens (§8.3); must mirror the tuning Def's
        // progressionLoreSeedEventDefNames defaults and the shipped XML list.
        private static readonly string[] AuditedProgressionEventTokens =
        {
            "PsylinkLevel", "XenotypeChanged", "GeneIdentityChanged",
            "BiotechMechlinkInstalled", "RoyalTitleGained", "RoyalTitlePromoted"
        };

        private sealed class CatalogRow
        {
            public string defName;
            public string label;
            public string text;
            public List<string> rawKeywords = new List<string>();
            public LoreSeedCandidate candidate;
        }

        private static List<CatalogRow> catalogCache;

        private static void TestLoreCatalogContract()
        {
            List<CatalogRow> catalog = LoadCatalog();
            AssertTrue("catalog is substantial (30-40 seeds)",
                catalog.Count >= 30 && catalog.Count <= 40);

            List<string> seen = new List<string>();
            for (int i = 0; i < catalog.Count; i++)
            {
                CatalogRow row = catalog[i];
                string id = row.defName;
                AssertTrue(id + " defName prefix", id.StartsWith("LoreSeed_", StringComparison.Ordinal));
                AssertTrue(id + " unique defName", !seen.Contains(id));
                seen.Add(id);
                AssertTrue(id + " label authored", !string.IsNullOrWhiteSpace(row.label));
                AssertTrue(id + " text authored within 200 chars",
                    !string.IsNullOrWhiteSpace(row.text) && row.text.Trim().Length <= 200);
                AssertTrue(id + " usage token",
                    row.candidate.usage == "initial" || row.candidate.usage == "progression"
                    || row.candidate.usage == "both");
                if (row.candidate.usage != "initial")
                {
                    AssertTrue(id + " progression usage declares event tokens",
                        row.candidate.progressionEventDefNames.Count > 0);
                    for (int j = 0; j < row.candidate.progressionEventDefNames.Count; j++)
                    {
                        AssertTrue(id + " progression token audited: "
                            + row.candidate.progressionEventDefNames[j],
                            IndexOfOrdinal(AuditedProgressionEventTokens,
                                row.candidate.progressionEventDefNames[j]) >= 0);
                    }
                }
                AssertTrue(id + " retention tier token",
                    row.candidate.retentionTier == "ordinary" || row.candidate.retentionTier == "core");
                AssertTrue(id + " positive finite weight",
                    row.candidate.weight > 0f && row.candidate.weight <= 1f);
                AssertTrue(id + " declares a known mutex group",
                    IndexOfOrdinal(KnownMutexGroups, row.candidate.mutexGroup) >= 0);
                AssertTrue(id + " has at least one tag", row.candidate.tags.Count > 0);
                for (int j = 0; j < row.candidate.tags.Count; j++)
                {
                    AssertTrue(id + " tag known: " + row.candidate.tags[j],
                        MemoryTagTokens.IsKnown(row.candidate.tags[j]));
                }

                AssertTrue(id + " keyword count within cap", row.rawKeywords.Count <= 8);
                for (int j = 0; j < row.rawKeywords.Count; j++)
                {
                    AssertTrue(id + " keyword in audited vocabulary: " + row.rawKeywords[j],
                        IndexOfOrdinal(AuditedKeywordVocabulary, row.rawKeywords[j]) >= 0);
                }

                if (row.candidate.retentionTier == "core")
                {
                    AssertTrue(id + " core seed carries exact high-confidence evidence",
                        row.candidate.backstoryDefNames.Count > 0
                        || row.candidate.xenotypeDefNames.Count > 0
                        || row.candidate.hediffDefNames.Count > 0);
                }
            }
        }

        private static void TestLoreCatalogReachabilityAndReservation()
        {
            List<CatalogRow> catalog = LoadCatalog();
            List<LoreSeedCandidate> candidates = new List<LoreSeedCandidate>();
            for (int i = 0; i < catalog.Count; i++)
            {
                candidates.Add(catalog[i].candidate);
            }

            // Supported base-game pawn fixtures (§7.1): each must reach a full roster with the
            // reserved pawn-specific slot satisfied.
            List<LoreSeedPawnFacts> fixtures = new List<LoreSeedPawnFacts>
            {
                LoreFacts(cats: Tags("Offworld")),
                LoreFacts(cats: Tags("Tribal", "ChildTribal")),
                LoreFacts(cats: Tags("ImperialCommon")),
                LoreFacts(cats: Tags("Outlander", "Civil")),
                LoreFacts(cats: Tags("Pirate")),
                LoreFacts(cats: Tags("Trader")),
                LoreFacts(cats: Tags("VatGrown")),
                LoreFacts(cats: Tags("Offworld"), backstories: Tags("GlitterworldSurgeon15")),
                LoreFacts(cats: Tags("Offworld"), backstories: Tags("MedievalLord57")),
                LoreFacts(cats: Tags("Civil"), hediffs: Tags("MechlinkImplant")),
                LoreFacts(cats: Tags("Civil"), hediffs: Tags("PsychicAmplifier")),
                LoreFacts(cats: Tags("Civil"), xeno: "Hussar"),
                LoreFacts(cats: Tags("Civil"), xeno: "Sanguophage"),
                LoreFacts(cats: Tags("Civil"), xeno: "Neanderthal"),
                LoreFacts(cats: Tags("Civil"), xeno: "Waster")
            };

            LoreSeedPolicy policy = new LoreSeedPolicy();
            for (int i = 0; i < fixtures.Count; i++)
            {
                List<LoreSeedPick> picks = LoreSeedPlanner.PlanInitial(candidates, fixtures[i], policy, 100 + i);
                AssertEqual("fixture " + i + " fills the full default roster", 4, picks.Count);
                bool specific = false;
                for (int j = 0; j < picks.Count; j++)
                {
                    specific = specific || picks[j].specific;
                }

                AssertTrue("fixture " + i + " satisfies the reserved specific slot", specific);
            }

            // Every catalog seed is reachable by at least one supported fixture (§14.2). Initial
            // seeds use the initial matrix; progression seeds must be reachable by their exact
            // event token plus a matching pawn fixture, and PlanProgression must actually pick one.
            for (int i = 0; i < candidates.Count; i++)
            {
                LoreSeedCandidate candidate = candidates[i];
                bool reachable = false;
                if (candidate.usage == "initial" || candidate.usage == "both")
                {
                    for (int j = 0; j < fixtures.Count && !reachable; j++)
                    {
                        reachable = LoreSeedPlanner.IsEligible(candidate, fixtures[j]);
                    }
                }

                if (candidate.usage == "progression" || candidate.usage == "both")
                {
                    for (int j = 0; j < fixtures.Count && !reachable; j++)
                    {
                        for (int t = 0; t < candidate.progressionEventDefNames.Count && !reachable; t++)
                        {
                            reachable = LoreSeedPlanner.IsEligibleForProgression(
                                candidate, fixtures[j], candidate.progressionEventDefNames[t]);
                        }
                    }
                }

                AssertTrue("seed reachable by a supported fixture: " + candidate.seedDefName,
                    reachable);
            }

            // Every audited progression event token yields exactly one pick for a plain fixture.
            for (int t = 0; t < AuditedProgressionEventTokens.Length; t++)
            {
                LoreSeedPick pick = LoreSeedPlanner.PlanProgression(
                    candidates,
                    LoreFacts(cats: Tags("Civil")),
                    new LoreSeedProgressionFacts
                    {
                        eventId = "evt-prog-" + t,
                        eventDefName = AuditedProgressionEventTokens[t]
                    },
                    policy,
                    500 + t);
                AssertTrue("progression token yields a catalog pick: "
                    + AuditedProgressionEventTokens[t], pick != null);
            }
        }

        private static void TestLoreCatalogRussianParity()
        {
            List<CatalogRow> catalog = LoadCatalog();
            string path = FindRepositoryFile(Path.Combine(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryLoreSeedDef", "DiaryLoreSeedDefs.xml"));
            XDocument document = XDocument.Load(path);
            XElement root = document.Root;
            AssertTrue("RU lore DefInjected root", root != null && root.Name.LocalName == "LanguageData");

            for (int i = 0; i < catalog.Count; i++)
            {
                string id = catalog[i].defName;
                string text = root.Element(id + ".text")?.Value;
                string label = root.Element(id + ".label")?.Value;
                AssertTrue("RU text authored for " + id, !string.IsNullOrWhiteSpace(text));
                AssertTrue("RU text length for " + id,
                    text != null && text.Trim().Length <= 200);
                AssertTrue("RU label authored for " + id, !string.IsNullOrWhiteSpace(label));
                AssertTrue("RU text is not a raw copy of the English for " + id,
                    !string.Equals(text, catalog[i].text, StringComparison.Ordinal));
            }

            int injectedCount = 0;
            foreach (XElement element in root.Elements())
            {
                if (element.Name.LocalName.EndsWith(".text", StringComparison.Ordinal))
                {
                    injectedCount++;
                    string defName = element.Name.LocalName.Substring(
                        0, element.Name.LocalName.Length - ".text".Length);
                    bool known = false;
                    for (int i = 0; i < catalog.Count && !known; i++)
                    {
                        known = catalog[i].defName == defName;
                    }

                    AssertTrue("RU injection matches a shipped seed: " + defName, known);
                }
            }

            AssertEqual("RU covers every seed exactly once", catalog.Count, injectedCount);
        }

        /// <summary>
        /// End-to-end recall smoke (§14.3): three representative seeds deposited as fragments
        /// (frozen tags + normalized keywords + implied lore tag) surface for their topical
        /// family's query and render their authored prose. Deliberately only three — per-row
        /// snapshots would be fragile; the contract/reachability tests cover the rest.
        /// </summary>
        private static void TestLoreCatalogRecallSmoke()
        {
            RunCatalogSmoke("LoreSeed_Mechanoid_Tribal",
                Tags("combat", "danger"), Tags("mechanoid"), "mechanoid raid");
            RunCatalogSmoke("LoreSeed_Psychic_Weather",
                Tags("psychic"), Tags("psychicdrone"), "psychic drone");
            RunCatalogSmoke("LoreSeed_Empire_Outsider",
                Tags("royalty"), Tags("empire"), "imperial event");
        }

        private static void RunCatalogSmoke(string seedDefName, string[] queryTags,
            string[] queryKeywords, string family)
        {
            CatalogRow row = null;
            List<CatalogRow> catalog = LoadCatalog();
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].defName == seedDefName)
                {
                    row = catalog[i];
                }
            }

            AssertTrue("smoke seed exists: " + seedDefName, row != null);

            MemoryFragmentSnapshot seed = new MemoryFragmentSnapshot
            {
                memoryId = "smoke-" + seedDefName,
                pawnId = "pawn1",
                sourceEventId = LoreSeedProvenance.InitialSourceEventId(seedDefName),
                tags = new List<string>(row.candidate.tags) { MemoryTagTokens.Lore },
                keywords = row.candidate.keywords,
                importance = 0.35f,
                createdTick = 0,
                lastRecalledTick = 0,
                text = row.text,
                loreSeedDefName = seedDefName,
                narrativeAgeOffsetTicks = 7200000
            };

            List<MemoryFragmentSnapshot> store = new List<MemoryFragmentSnapshot>
            {
                seed,
                Frag("f1", 0, Tags("work"), Tags("fields"), 0.4f, "Working the fields."),
                Frag("f2", 0, Tags("romance"), Tags("kisses"), 0.4f, "A shy first kiss."),
                Frag("f3", 0, Tags("arrival"), Tags("greeting"), 0.4f, "A stranger joined us.")
            };

            MemoryRecallResult result = MemoryRecallSelector.Recall(
                Query(queryTags, queryKeywords, 100000), store, RecallPolicy());
            AssertEqual(family + " smoke surfaces the lore seed", 1, result.picks.Count);
            AssertEqual(family + " smoke picks the seed row", seed.memoryId, result.picks[0].memoryId);
            AssertTrue(family + " smoke renders the authored prose",
                result.memoryContext.Contains(row.text));
        }

        private static List<CatalogRow> LoadCatalog()
        {
            if (catalogCache != null)
            {
                return catalogCache;
            }

            string path = FindRepositoryFile(Path.Combine("1.6", "Defs", "DiaryLoreSeedDefs.xml"));
            XDocument document = XDocument.Load(path);
            List<CatalogRow> catalog = new List<CatalogRow>();
            foreach (XElement def in document.Root.Elements("PawnDiary.DiaryLoreSeedDef"))
            {
                LoreSeedCandidate candidate = new LoreSeedCandidate
                {
                    seedDefName = def.Element("defName")?.Value ?? string.Empty,
                    fallbackText = def.Element("text")?.Value ?? string.Empty,
                    usage = def.Element("usage")?.Value ?? string.Empty,
                    retentionTier = def.Element("retentionTier")?.Value ?? string.Empty,
                    weight = float.Parse(def.Element("weight")?.Value ?? "0",
                        CultureInfo.InvariantCulture),
                    mutexGroup = def.Element("mutexGroup")?.Value ?? string.Empty,
                    tags = XmlListValues(def, "tags"),
                    backstoryCategories = XmlListValues(def, "backstoryCategories"),
                    excludeBackstoryCategories = XmlListValues(def, "excludeBackstoryCategories"),
                    backstoryDefNames = XmlListValues(def, "backstoryDefNames"),
                    excludeBackstoryDefNames = XmlListValues(def, "excludeBackstoryDefNames"),
                    xenotypeDefNames = XmlListValues(def, "xenotypeDefNames"),
                    hediffDefNames = XmlListValues(def, "hediffDefNames"),
                    progressionEventDefNames = XmlListValues(def, "progressionEventDefNames")
                };
                List<string> rawKeywords = XmlListValues(def, "keywords");
                candidate.keywords = MemoryExtraction.NormalizeAuthoredKeywords(rawKeywords, 8);
                catalog.Add(new CatalogRow
                {
                    defName = candidate.seedDefName,
                    label = def.Element("label")?.Value ?? string.Empty,
                    text = candidate.fallbackText.Trim(),
                    rawKeywords = rawKeywords,
                    candidate = candidate
                });
            }

            catalogCache = catalog;
            return catalog;
        }

        private static List<string> XmlListValues(XElement parent, string listName)
        {
            List<string> values = new List<string>();
            XElement list = parent.Element(listName);
            if (list == null)
            {
                return values;
            }

            foreach (XElement li in list.Elements("li"))
            {
                if (!string.IsNullOrWhiteSpace(li.Value))
                {
                    values.Add(li.Value.Trim());
                }
            }

            return values;
        }

        private static int IndexOfOrdinal(string[] values, string target)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i], target, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static LoreSeedCandidate LoreSeed(string name, string usage = "initial",
            string tier = "ordinary", float weight = 1f, string mutex = "",
            string[] cats = null, string[] excludeCats = null,
            string[] backstories = null, string[] excludeBackstories = null,
            string[] xenos = null, string[] hediffs = null)
        {
            return new LoreSeedCandidate
            {
                seedDefName = name,
                fallbackText = "Seed text for " + name + ".",
                usage = usage,
                retentionTier = tier,
                weight = weight,
                mutexGroup = mutex,
                backstoryCategories = ListOf(cats ?? new string[0]),
                excludeBackstoryCategories = ListOf(excludeCats ?? new string[0]),
                backstoryDefNames = ListOf(backstories ?? new string[0]),
                excludeBackstoryDefNames = ListOf(excludeBackstories ?? new string[0]),
                xenotypeDefNames = ListOf(xenos ?? new string[0]),
                hediffDefNames = ListOf(hediffs ?? new string[0])
            };
        }

        private static LoreSeedPawnFacts LoreFacts(string[] cats = null, string[] backstories = null,
            string xeno = "", string[] hediffs = null)
        {
            return new LoreSeedPawnFacts
            {
                pawnId = "pawn1",
                biologicalAgeTicks = 100000000L,
                backstoryCategories = ListOf(cats ?? new string[0]),
                backstoryDefNames = ListOf(backstories ?? new string[0]),
                xenotypeDefName = xeno,
                hediffDefNames = ListOf(hediffs ?? new string[0])
            };
        }

        private static int CountCore(List<LoreSeedPick> picks)
        {
            int count = 0;
            for (int i = 0; i < picks.Count; i++)
            {
                if (picks[i].core) count++;
            }

            return count;
        }

        private static bool ContainsPick(List<LoreSeedPick> picks, string seedDefName)
        {
            for (int i = 0; i < picks.Count; i++)
            {
                if (picks[i].seedDefName == seedDefName) return true;
            }

            return false;
        }

        // ---------------------------------------------------------------------------------------------
        // Builders and assertions
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// The §8.3 worked-example store: a combat memory keyworded "yorick" anchors the direct
        /// pick; a cooking memory sharing only "yorick"/"rifle" keywords becomes reachable through
        /// the 1-hop spread despite zero overlap with the raid query.
        /// </summary>
        private static List<MemoryFragmentSnapshot> BridgeStore()
        {
            return new List<MemoryFragmentSnapshot>
            {
                Frag("raid", 2000000, Tags("combat", "danger", "death"), Tags("yorick", "rifle"),
                    0.8f, "The raid where Yorick fell.", sourceEventId: "evt-raid"),
                Frag("cooking", 2000000, Tags("social", "joy"), Tags("yorick", "rifle", "cooking"),
                    1.0f, "Yorick showing me his rifle collection.", sourceEventId: "evt-cooking"),
                Frag("f1", 2000000, Tags("work"), Tags("fields"), 0.3f, "Working the fields."),
                Frag("f2", 2000000, Tags("illness"), Tags("flu"), 0.3f, "A bad flu week."),
                Frag("f3", 2000000, Tags("family"), Tags("mother"), 0.3f, "A letter from mother.")
            };
        }

        /// <summary>One perfect-overlap fragment plus fillers, created at tick 1000.</summary>
        private static List<MemoryFragmentSnapshot> PerfectOverlapStore()
        {
            return new List<MemoryFragmentSnapshot>
            {
                Frag("m1", 1000, Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), 1.0f,
                    "The firefight at the pass."),
                Frag("f1", 1000, Tags("work"), Tags("fields"), 0.4f, "Working the fields."),
                Frag("f2", 1000, Tags("illness"), Tags("flu"), 0.4f, "A bad flu week."),
                Frag("f3", 1000, Tags("family"), Tags("mother"), 0.4f, "A letter from mother.")
            };
        }

        /// <summary>Policy with deterministic gates: recall always open, spread closed unless asked.</summary>
        private static MemoryPolicySnapshot RecallPolicy(float recallGate = 1f, float spreadGate = 0f)
        {
            MemoryPolicySnapshot policy = MemoryPolicySnapshot.CreateDefault();
            policy.recallGateChance = recallGate;
            policy.spreadGateChance = spreadGate;
            return policy;
        }

        private static MemoryExtractionInput CueInput(string cue)
        {
            return new MemoryExtractionInput { povName = "Daisy", solo = true, colorCue = cue };
        }

        private static MemoryRecallQuery Query(string[] tags, string[] keywords, int currentTick,
            int seed = 7, string currentEventId = "evt-now")
        {
            return new MemoryRecallQuery
            {
                tags = ListOf(tags),
                keywords = ListOf(keywords),
                currentEventId = currentEventId,
                currentTick = currentTick,
                seed = seed
            };
        }

        private static MemoryFragmentSnapshot Frag(string id, int createdTick, string[] tags,
            string[] keywords, float importance, string text, string sourceEventId = "evt-src",
            string pawnId = "pawn1", int lastRecalledTick = -1, int recallCount = 0)
        {
            return new MemoryFragmentSnapshot
            {
                memoryId = id,
                pawnId = pawnId,
                sourceEventId = sourceEventId,
                tags = ListOf(tags),
                keywords = ListOf(keywords),
                importance = importance,
                createdTick = createdTick,
                lastRecalledTick = lastRecalledTick < 0 ? createdTick : lastRecalledTick,
                recallCount = recallCount,
                text = text
            };
        }

        private static string[] Tags(params string[] values)
        {
            return values;
        }

        private static List<string> ListOf(string[] values)
        {
            return new List<string>(values);
        }

        private static void AssertAgeLabel(MemoryPolicySnapshot policy, int age, string expectedLabel)
        {
            const int current = 10000000;
            List<MemoryFragmentSnapshot> store = new List<MemoryFragmentSnapshot>
            {
                Frag("aged", current - age, Tags("combat", "danger"), Tags("yorick", "rifle", "fire"),
                    1.0f, "An old firefight."),
                Frag("f1", current - age, Tags("work"), Tags("fields"), 0.4f, "Working the fields."),
                Frag("f2", current - age, Tags("illness"), Tags("flu"), 0.4f, "A bad flu week."),
                Frag("f3", current - age, Tags("family"), Tags("mother"), 0.4f, "A letter from mother.")
            };
            MemoryRecallResult result = MemoryRecallSelector.Recall(
                Query(Tags("combat", "danger"), Tags("yorick", "rifle", "fire"), current), store, policy);
            AssertEqual("age " + age + " renders its band", 1, result.picks.Count);
            AssertEqual("age " + age + " label",
                "- (" + expectedLabel + ") An old firefight.", result.memoryContext);
        }

        private static void AssertImportance(string label, MemoryPolicySnapshot policy,
            float expected, string cue = null)
        {
            MemoryExtractionInput input = CueInput(cue ?? label);
            AssertNear("importance: " + label, expected,
                MemoryExtraction.Extract(input, policy).importance, 0.0001f);
        }

        private static void AssertTags(string label, MemoryPolicySnapshot policy,
            MemoryExtractionInput input, params string[] expected)
        {
            AssertSequence(label, expected, MemoryExtraction.Extract(input, policy).tags);
        }

        private static float CueImportance(MemoryPolicySnapshot policy, string cue)
        {
            for (int i = 0; i < policy.cueImportance.Count; i++)
            {
                if (policy.cueImportance[i].cue == cue)
                {
                    return policy.cueImportance[i].importance;
                }
            }

            throw new InvalidOperationException("missing cue row " + cue);
        }

        private static string FindRepositoryFile(string relativePath)
        {
            string[] starts = { Environment.CurrentDirectory, AppContext.BaseDirectory };
            for (int start = 0; start < starts.Length; start++)
            {
                DirectoryInfo directory = new DirectoryInfo(starts[start]);
                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, relativePath);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    directory = directory.Parent;
                }
            }

            throw new InvalidOperationException("could not locate repository file " + relativePath);
        }

        private static List<XElement> XmlRows(XElement def, string tableName)
        {
            XElement table = def?.Element(tableName);
            return table == null ? new List<XElement>() : new List<XElement>(table.Elements("li"));
        }

        private static List<string> XmlStringList(XElement list)
        {
            List<string> values = new List<string>();
            if (list == null)
            {
                return values;
            }

            foreach (XElement row in list.Elements("li"))
            {
                values.Add(row.Value.Trim());
            }

            return values;
        }

        private static string XmlValue(XElement parent, string name)
        {
            XElement value = parent?.Element(name);
            if (value == null)
            {
                throw new InvalidOperationException("memory tuning XML is missing " + name);
            }

            return value.Value.Trim();
        }

        private static int XmlInt(XElement parent, string name)
        {
            return int.Parse(XmlValue(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static float XmlFloat(XElement parent, string name)
        {
            return float.Parse(XmlValue(parent, name), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void AssertXmlInt(XElement def, string name, int expected)
        {
            AssertEqual("XML default " + name, expected, XmlInt(def, name));
        }

        private static void AssertXmlFloat(XElement def, string name, float expected)
        {
            AssertNear("XML default " + name, expected, XmlFloat(def, name), 0.0001f);
        }

        private static void AssertXmlBool(XElement def, string name, bool expected)
        {
            AssertEqual("XML default " + name, expected,
                bool.Parse(XmlValue(def, name)));
        }

        private static int CountKnownTokens(string[] candidates)
        {
            int count = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (MemoryTagTokens.IsKnown(candidates[i])) count++;
            }

            return count;
        }

        /// <summary>Finds the first seed whose seeded Random sequence matches the predicate.</summary>
        private static int SeedWhere(Func<Random, bool> predicate)
        {
            for (int seed = 1; seed < 200000; seed++)
            {
                if (predicate(new Random(seed)))
                {
                    return seed;
                }
            }

            throw new InvalidOperationException("no seed matched the predicate");
        }

        private static bool ContainsPrefix(List<string> values, string prefix)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] != null && values[i].StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertSequence(string label, string[] expected, List<string> actual)
        {
            assertions++;
            string actualJoined = string.Join("|", actual.ToArray());
            string expectedJoined = string.Join("|", expected);
            if (actualJoined != expectedJoined)
            {
                throw new InvalidOperationException(
                    label + ": expected [" + expectedJoined + "], got [" + actualJoined + "]");
            }
        }

        private static void AssertContains(string label, List<string> values, string expected)
        {
            assertions++;
            if (!values.Contains(expected))
            {
                throw new InvalidOperationException(label + ": missing " + expected);
            }
        }

        private static void AssertNear(string label, float expected, float actual, float tolerance)
        {
            assertions++;
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    label + ": expected " + expected + ", got " + actual);
            }
        }

        private static void AssertEqual<T>(string label, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(label + ": expected " + expected + ", got " + actual);
            }
        }

        private static void AssertTrue(string label, bool value)
        {
            assertions++;
            if (!value)
            {
                throw new InvalidOperationException(label);
            }
        }
    }
}

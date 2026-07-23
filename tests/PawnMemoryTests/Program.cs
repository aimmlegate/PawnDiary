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

// Standalone no-RimWorld tests for Biotech B1 and the Phase 5 pure gene-policy foundation. Besides
// exercising pure decisions, this suite parses shipped XML/localization so stable IDs, package
// gates, policy caps, band prose, and exact classifier ownership cannot drift from the contracts.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PawnDiary.Capture;

namespace DiaryBiotechPolicyTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestStableSchemaAndArcKeys();
            TestGrowthDiffAndTruthBoundaries();
            TestOpportunityBands();
            TestPendingGrowthNormalization();
            TestGrowthRecordMatching();
            TestFamilyArcObservationAndRetention();
            TestSupporterSelectionAndWriterShapes();
            TestBirthWriterSelection();
            TestBirthCorrelationClassification();
            TestBirthArcAndPendingOwnership();
            TestSettingsInheritance();
            TestContextFormatting();
            TestGeneSalienceCardinalityAndDeltas();
            TestGeneSalienceDiversityCorrectionsAndDeterminism();
            TestGeneSalienceFilteringAndTextCaps();
            TestGeneObservationBaselineAndNormalization();
            TestGeneTransitionDiffFallbackAndContext();
            TestMechanitorLifecyclePolicy();
            TestPsychicBondLifecyclePolicy();
            TestDeathrestInterruptionPolicy();
            TestShippedXmlPolicyAndLocalization();
            Console.WriteLine("DiaryBiotechPolicyTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void TestGeneSalienceCardinalityAndDeltas()
        {
            GeneSaliencePolicySnapshot policy = GeneSaliencePolicySnapshot.CreateDefault();
            AssertEqual("zero genes produce no themes", 0,
                GeneSaliencePolicy.Select(new GeneIdentitySnapshot(), null, policy).Count);

            GeneIdentitySnapshot one = new GeneIdentitySnapshot();
            one.genes.Add(Gene("Gene_Only", "Only", GeneCategoryTokens.Social));
            AssertSequence("one usable gene stays one theme", new[] { "Gene_Only" },
                GeneSaliencePolicy.Select(one, null, policy).Select(theme => theme.defName));

            GeneIdentitySnapshot many = new GeneIdentitySnapshot();
            many.genes.Add(Gene("Gene_AbilityA", "Ability A", GeneCategoryTokens.Ability));
            many.genes.Add(Gene("Gene_Trait", "Trait", GeneCategoryTokens.Trait));
            many.genes.Add(Gene("Gene_Need", "Need", GeneCategoryTokens.Need));
            many.genes.Add(Gene("Gene_Social", "Social", GeneCategoryTokens.Social));
            many.genes.Add(Gene("Gene_Stat", "Stat", GeneCategoryTokens.Stat));
            many.genes.Add(Gene("Gene_Other", "Other", GeneCategoryTokens.Other));
            List<GeneTheme> bounded = GeneSaliencePolicy.Select(many, null, policy);
            AssertEqual("many genes never expose full membership", 4, bounded.Count);
            AssertSequence("highest diverse categories selected", new[]
            {
                "Gene_AbilityA", "Gene_Trait", "Gene_Need", "Gene_Social"
            }, bounded.Select(theme => theme.defName));

            GeneMutationSnapshot mutation = new GeneMutationSnapshot();
            mutation.addedGenes.Add(Gene("Gene_Added", "Added", GeneCategoryTokens.Other));
            mutation.removedGenes.Add(Gene("Gene_Removed", "Removed", GeneCategoryTokens.Stat));
            List<GeneTheme> changed = GeneSaliencePolicy.Select(many, mutation, policy);
            AssertTrue("both exact deltas outrank unchanged candidates",
                changed.Take(2).Select(theme => theme.defName)
                    .OrderBy(value => value).SequenceEqual(new[] { "Gene_Added", "Gene_Removed" }));
            AssertEqual("added token preserved", GeneChangeTokens.Added,
                changed.Single(theme => theme.defName == "Gene_Added").change);
            AssertEqual("removed token preserved", GeneChangeTokens.Removed,
                changed.Single(theme => theme.defName == "Gene_Removed").change);

            GeneIdentitySnapshot duplicateMembership = new GeneIdentitySnapshot();
            duplicateMembership.genes.Add(Gene("Gene_Added", "Added", GeneCategoryTokens.Other));
            AssertEqual("added membership is emitted once", 1,
                GeneSaliencePolicy.Select(duplicateMembership, mutation, policy)
                    .Count(theme => theme.defName == "Gene_Added"));
        }

        private static void TestGeneSalienceDiversityCorrectionsAndDeterminism()
        {
            GeneSaliencePolicySnapshot policy = GeneSaliencePolicySnapshot.CreateDefault();
            GeneIdentitySnapshot snapshot = new GeneIdentitySnapshot();
            snapshot.genes.Add(Gene("Gene_AbilityB", "Ability B", GeneCategoryTokens.Ability));
            snapshot.genes.Add(Gene("Gene_AbilityA", "Ability A", GeneCategoryTokens.Ability));
            snapshot.genes.Add(Gene("Gene_Trait", "Trait", GeneCategoryTokens.Trait));
            snapshot.genes.Add(Gene("Gene_Excluded", "Excluded", GeneCategoryTokens.Resource));
            snapshot.genes.Add(Gene("Gene_Forced", "Forced", GeneCategoryTokens.Other));
            policy.excludeDefNames.Add("gene_excluded");
            policy.forceIncludeDefNames.Add("gene_forced");

            List<GeneTheme> selected = GeneSaliencePolicy.Select(snapshot, null, policy);
            AssertEqual("force correction outranks category weights", "Gene_Forced", selected[0].defName);
            AssertTrue("exclude correction removes exact Def", selected.All(theme => theme.defName != "Gene_Excluded"));
            AssertTrue("category diversity beats second ability",
                selected.FindIndex(theme => theme.defName == "Gene_Trait")
                    < selected.FindIndex(theme => theme.defName == "Gene_AbilityB"));

            GeneIdentitySnapshot ties = new GeneIdentitySnapshot();
            ties.genes.Add(Gene("Gene_Zed", "Zed", GeneCategoryTokens.Social));
            ties.genes.Add(Gene("Gene_Alpha", "Alpha", GeneCategoryTokens.Social));
            List<GeneTheme> first = GeneSaliencePolicy.Select(ties, null, policy);
            ties.genes.Reverse();
            List<GeneTheme> second = GeneSaliencePolicy.Select(ties, null, policy);
            AssertSequence("tie order ignores input enumeration", first.Select(theme => theme.defName),
                second.Select(theme => theme.defName));
            AssertEqual("ordinal Def name breaks exact tie", "Gene_Alpha", first[0].defName);

            policy.allowDuplicateCategories.Add(GeneCategoryTokens.Social);
            AssertSequence("XML exception permits same-category ordering",
                new[] { "Gene_Alpha", "Gene_Zed" },
                GeneSaliencePolicy.Select(ties, null, policy).Select(theme => theme.defName));
        }

        private static void TestGeneSalienceFilteringAndTextCaps()
        {
            GeneSaliencePolicySnapshot policy = GeneSaliencePolicySnapshot.CreateDefault();
            policy.labelCharacterLimit = 6;
            policy.descriptionCharacterLimit = 9;
            policy.totalTextCharacterLimit = 13;
            GeneIdentitySnapshot snapshot = new GeneIdentitySnapshot();
            snapshot.genes.Add(Gene("Gene_Valid", "  Long\t label  ", GeneCategoryTokens.Trait,
                "long\r\n description with repeated   spaces"));
            GeneFact hidden = Gene("Gene_Hidden", "Hidden", GeneCategoryTokens.Ability);
            hidden.hidden = true;
            snapshot.genes.Add(hidden);
            GeneFact inactive = Gene("Gene_Inactive", "Inactive", GeneCategoryTokens.Ability);
            inactive.active = false;
            snapshot.genes.Add(inactive);
            GeneFact suppressed = Gene("Gene_Suppressed", "Suppressed", GeneCategoryTokens.Ability);
            suppressed.suppressed = true;
            snapshot.genes.Add(suppressed);
            snapshot.genes.Add(Gene(string.Empty, "Missing key", GeneCategoryTokens.Ability));

            List<GeneTheme> selected = GeneSaliencePolicy.Select(snapshot, null, policy);
            AssertEqual("bookkeeping and malformed genes are omitted", 1, selected.Count);
            AssertEqual("label whitespace cleaned and capped", "Long l", selected[0].label);
            AssertEqual("description consumes remaining total cap", "long de", selected[0].description);
            AssertTrue("combined text obeys XML total cap",
                selected.Sum(theme => theme.label.Length + theme.description.Length) <= 13);
            AssertTrue("control whitespace never reaches selected text",
                !selected[0].label.Contains("\t") && !selected[0].description.Contains("\r")
                    && !selected[0].description.Contains("\n"));
        }

        private static void TestGeneObservationBaselineAndNormalization()
        {
            GeneIdentitySnapshot identity = new GeneIdentitySnapshot
            {
                xenotypeDefName = " CustomXenotype ",
                xenotypeLabel = "  Long\tcustom\r\nidentity  ",
                installedGeneDefNames = new List<string>
                {
                    "Gene_Zed", "gene_alpha", "Gene_Alpha", "", "Gene_Middle"
                },
                genes = new List<GeneFact>
                {
                    Gene("Gene_Zed", "Zed", GeneCategoryTokens.Other),
                    Gene("gene_alpha", "Alpha", GeneCategoryTokens.Other),
                    Gene("Gene_Alpha", "Duplicate", GeneCategoryTokens.Other),
                    Gene("", "Malformed", GeneCategoryTokens.Other),
                    Gene("Gene_Middle", "Middle", GeneCategoryTokens.Other)
                }
            };

            GeneIdentityObservationSnapshot observed = GeneIdentityObservationPolicy.Observe(
                identity, maximumGeneDefNames: 2, labelCharacterLimit: 11);
            AssertEqual("gene observation version", GeneIdentityObservationPolicy.CurrentVersion,
                observed.observationVersion);
            AssertEqual("gene observation xenotype Def trimmed", "CustomXenotype", observed.xenotypeDefName);
            AssertEqual("gene observation label cleaned and capped", "Long custom", observed.xenotypeLabel);
            AssertSequence("gene observation membership deduped sorted and capped",
                new[] { "gene_alpha", "Gene_Middle" }, observed.geneDefNames);
            AssertTrue("gene observation records incomplete bounded membership",
                observed.membershipTruncated);
            AssertEqual("gene observation does not mutate source membership", 5,
                identity.installedGeneDefNames.Count);
            AssertTrue("version marker distinguishes a valid empty membership",
                GeneIdentityObservationPolicy.HasCurrentBaseline(GeneIdentityObservationPolicy.Observe(
                    new GeneIdentitySnapshot(), 10, 80)));

            GeneIdentityObservationSnapshot malformed = new GeneIdentityObservationSnapshot
            {
                observationVersion = -7,
                xenotypeDefName = "  Baseliner\n",
                xenotypeLabel = " base\t liner ",
                geneDefNames = new List<string> { " Gene_B ", null, "gene_b", "Gene_A" }
            };
            GeneIdentityObservationSnapshot normalized = GeneIdentityObservationPolicy.Normalize(
                malformed, maximumGeneDefNames: 10, labelCharacterLimit: 80);
            AssertEqual("malformed observation version stays uninitialized", 0, normalized.observationVersion);
            AssertEqual("normalized xenotype Def one line", "Baseliner", normalized.xenotypeDefName);
            AssertEqual("normalized xenotype label one line", "base liner", normalized.xenotypeLabel);
            AssertSequence("normalized membership is unique and deterministic",
                new[] { "Gene_A", "Gene_B" }, normalized.geneDefNames);
            AssertTrue("uninitialized row is not mistaken for empty baseline",
                !GeneIdentityObservationPolicy.HasCurrentBaseline(normalized));
            GeneIdentityObservationSnapshot repairedOverflow = GeneIdentityObservationPolicy.Normalize(
                new GeneIdentityObservationSnapshot
                {
                    observationVersion = GeneIdentityObservationPolicy.CurrentVersion,
                    geneDefNames = new List<string> { "Gene_A", "Gene_B", "Gene_C" }
                },
                maximumGeneDefNames: 2,
                labelCharacterLimit: 80);
            AssertTrue("normalization marks corrupt overflow as incomplete",
                repairedOverflow.membershipTruncated);
            AssertTrue("version-one baseline is re-established under the truncation schema",
                !GeneIdentityObservationPolicy.HasCurrentBaseline(new GeneIdentityObservationSnapshot
                {
                    observationVersion = 1
                }));
        }

        private static void TestGeneTransitionDiffFallbackAndContext()
        {
            GeneSaliencePolicySnapshot policy = GeneSaliencePolicySnapshot.CreateDefault();
            GeneIdentitySnapshot before = new GeneIdentitySnapshot
            {
                xenotypeDefName = "Baseliner",
                xenotypeLabel = "Baseliner"
            };
            before.installedGeneDefNames.AddRange(new[] { "Gene_Removed", "Gene_Stable" });
            before.genes.Add(Gene("Gene_Removed", "Old; voice", GeneCategoryTokens.Social));
            before.genes.Add(Gene("Gene_Stable", "Stable", GeneCategoryTokens.Stat));

            GeneIdentitySnapshot after = new GeneIdentitySnapshot
            {
                xenotypeDefName = "Custom",
                xenotypeLabel = "Night=kin\r\nPrime"
            };
            after.installedGeneDefNames.AddRange(new[] { "Gene_Added", "Gene_Stable", "Gene_Unselected" });
            after.genes.Add(Gene("Gene_Added", "New; hunger", GeneCategoryTokens.Need,
                "Needs=hemogen; after dusk"));
            after.genes.Add(Gene("Gene_Stable", "Stable", GeneCategoryTokens.Stat));
            after.genes.Add(Gene("Gene_Unselected", "Unselected", GeneCategoryTokens.Other));

            GeneIdentityTransitionDecision decision = GeneIdentityTransitionPolicy.Evaluate(
                before, after, policy);
            AssertTrue("stable xenotype Def transition detected", decision.xenotypeIdentityChanged);
            AssertEqual("one installed gene added", 2, decision.addedGeneCount);
            AssertEqual("one installed gene removed", 1, decision.removedGeneCount);
            AssertSequence("exact added facts deterministic", new[] { "Gene_Added", "Gene_Unselected" },
                decision.mutation.addedGenes.Select(fact => fact.defName));
            AssertSequence("removed fact retains before-state prose", new[] { "Gene_Removed" },
                decision.mutation.removedGenes.Select(fact => fact.defName));
            AssertEqual("delta theme outranks unchanged fact", "Gene_Added", decision.themes[0].defName);
            AssertTrue("exact transition emits", decision.HasAnyChange);
            AssertTrue("xenotype transition ignores fallback threshold",
                GeneIdentityTransitionPolicy.ShouldEmitFallback(decision, 99));

            GeneIdentitySnapshot translatedLabel = new GeneIdentitySnapshot
            {
                xenotypeDefName = "Custom",
                xenotypeLabel = "Localized different label"
            };
            translatedLabel.installedGeneDefNames.AddRange(after.installedGeneDefNames);
            translatedLabel.genes.AddRange(after.genes);
            GeneIdentityTransitionDecision languageOnly = GeneIdentityTransitionPolicy.Evaluate(
                after, translatedLabel, policy);
            AssertTrue("localized label change with stable Def is silent", !languageOnly.HasAnyChange);

            GeneIdentitySnapshot oneAdded = GeneIdentityTransitionPolicy.FromObservation(
                new GeneIdentityObservationSnapshot
                {
                    observationVersion = GeneIdentityObservationPolicy.CurrentVersion,
                    xenotypeDefName = "Baseliner",
                    xenotypeLabel = "Baseliner",
                    geneDefNames = new List<string> { "Gene_Stable" }
                });
            GeneIdentitySnapshot oneAddedAfter = new GeneIdentitySnapshot
            {
                xenotypeDefName = "Baseliner",
                xenotypeLabel = "Baseliner",
                installedGeneDefNames = new List<string> { "Gene_Stable", "Gene_Added" },
                genes = new List<GeneFact> { Gene("Gene_Added", "Added", GeneCategoryTokens.Trait) }
            };
            GeneIdentityTransitionDecision smallFallback = GeneIdentityTransitionPolicy.Evaluate(
                oneAdded, oneAddedAfter, policy);
            AssertTrue("one-gene fallback stays below configured significance",
                !GeneIdentityTransitionPolicy.ShouldEmitFallback(smallFallback, 2));
            AssertTrue("XML can admit one-gene fallback",
                GeneIdentityTransitionPolicy.ShouldEmitFallback(smallFallback, 1));

            GeneIdentitySnapshot suppressionOnly = new GeneIdentitySnapshot
            {
                xenotypeDefName = after.xenotypeDefName,
                xenotypeLabel = after.xenotypeLabel,
                installedGeneDefNames = new List<string>(after.installedGeneDefNames),
                genes = new List<GeneFact>()
            };
            AssertTrue("active/suppressed recalculation never becomes membership mutation",
                !GeneIdentityTransitionPolicy.Evaluate(after, suppressionOnly, policy).HasAnyChange);

            // The configured persisted cap is a storage window, not a complete set. Adding an earlier
            // name may displace the last saved row, so fallback must not fabricate that row's removal.
            GeneIdentitySnapshot boundedBefore = GeneIdentityTransitionPolicy.FromObservation(
                GeneIdentityObservationPolicy.Observe(
                    new GeneIdentitySnapshot
                    {
                        installedGeneDefNames = new List<string> { "Gene_B", "Gene_C" }
                    },
                    maximumGeneDefNames: 1,
                    labelCharacterLimit: 80));
            GeneIdentitySnapshot boundedAfter = new GeneIdentitySnapshot
            {
                installedGeneDefNames = new List<string> { "Gene_A", "Gene_B", "Gene_C" }
            };
            GeneIdentityTransitionDecision uncertainBounded = GeneIdentityTransitionPolicy.Evaluate(
                boundedBefore, boundedAfter, policy);
            AssertTrue("incomplete saved membership cannot invent fallback deltas",
                !uncertainBounded.HasAnyChange);

            GeneIdentitySnapshot exactBefore = new GeneIdentitySnapshot
            {
                installedGeneDefNames = new List<string> { "Gene_B", "Gene_C" }
            };
            GeneIdentityTransitionDecision exactAddition = GeneIdentityTransitionPolicy.Evaluate(
                exactBefore, boundedAfter, policy);
            AssertEqual("complete exact membership keeps the real addition", 1,
                exactAddition.addedGeneCount);
            AssertEqual("complete exact membership invents no displaced removal", 0,
                exactAddition.removedGeneCount);

            AssertEqual("gene event dedup key is stable across capture ticks",
                "progression-gene|Pawn_7|xenogerm_reimplant",
                GeneIdentityEventKeys.DedupKey(" Pawn_7 ", " xenogerm_reimplant "));

            string context = GeneIdentityContextFormatter.Format(
                before,
                after,
                decision,
                GeneChangeCauseTokens.XenogermReimplant,
                "Caster; Name=One",
                "Pawn=7;bad",
                20);
            AssertContains("context marks identity transition", context, "gene_identity_transition=true");
            AssertContains("context stores exact cause", context,
                "gene_change_cause=" + GeneChangeCauseTokens.XenogermReimplant);
            AssertContains("context stores selected theme", context, "gene_theme_1=New, hunger");
            AssertContains("context stores theme change", context, "gene_theme_change_1=added");
            AssertContains("context stores narrative facet", context,
                "narrative_facets=identity_transition");
            AssertTrue("context values cannot inject fields",
                !context.Contains("; Name=") && !context.Contains(";bad")
                && context.Contains("other_pawn=Caster, Name:One"));
            AssertTrue("context never serializes complete membership",
                !context.Contains("gene_membership") && !context.Contains("Gene_Stable"));
            AssertEqual("outer progression label is separator-safe and capped", "Night:ki",
                GeneIdentityContextFormatter.CleanField("Night=kin;raw", 8));
        }

        private static void TestGrowthRecordMatching()
        {
            AssertTrue("supporter-owned growth still matches child identity",
                GrowthRecordPolicy.Matches(
                    BiotechEventDefNames.GrowthMoment,
                    "Child_1",
                    "10",
                    "Child_1",
                    10));
            AssertTrue("different child cannot satisfy growth backstop",
                !GrowthRecordPolicy.Matches(
                    BiotechEventDefNames.GrowthMoment,
                    "Child_2",
                    "10",
                    "Child_1",
                    10));
            AssertTrue("different birthday cannot satisfy growth backstop",
                !GrowthRecordPolicy.Matches(
                    BiotechEventDefNames.GrowthMoment,
                    "Child_1",
                    "7",
                    "Child_1",
                    10));
            AssertTrue("ordinary birthday cannot satisfy canonical growth backstop",
                !GrowthRecordPolicy.Matches(
                    "Birthday",
                    "Child_1",
                    "10",
                    "Child_1",
                    10));
        }

        private static void TestBirthCorrelationClassification()
        {
            BiotechPolicySnapshot policy = BiotechPolicySnapshot.CreateDefault();
            AssertTrue("mature birth exact Def accepted",
                BirthCorrelationPolicy.IsMatureBirthDef("BabyBorn", policy));
            AssertTrue("unrelated Tale does not enter birth ownership",
                !BirthCorrelationPolicy.IsMatureBirthDef("Birthday", policy));
            AssertEqual("birther miscarriage role", BiotechFamilyRoleTokens.Birther,
                BirthCorrelationPolicy.MiscarriageRole(
                    "Miscarried", "Birther", "Birther", "Mother", "Father", policy));
            AssertEqual("genetic mother partner-loss role", BiotechFamilyRoleTokens.GeneticMother,
                BirthCorrelationPolicy.MiscarriageRole(
                    "PartnerMiscarried", "Mother", "Birther", "Mother", "Father", policy));
            AssertEqual("father partner-loss role", BiotechFamilyRoleTokens.Father,
                BirthCorrelationPolicy.MiscarriageRole(
                    "PartnerMiscarried", "Father", "Birther", "Mother", "Father", policy));
            AssertEqual("wrong pawn receives no miscarriage role", string.Empty,
                BirthCorrelationPolicy.MiscarriageRole(
                    "Miscarried", "Father", "Birther", "Mother", "Father", policy));

            BiotechPolicySnapshot custom = BiotechPolicySnapshot.CreateDefault();
            custom.matureBirthDefNames.Clear();
            custom.matureBirthDefNames.Add("ModdedBirth");
            custom.miscarriageBirtherThoughtDefNames.Clear();
            custom.miscarriageBirtherThoughtDefNames.Add("ModdedLoss");
            AssertTrue("XML snapshot can replace mature classifier",
                BirthCorrelationPolicy.IsMatureBirthDef("ModdedBirth", custom));
            AssertTrue("replaced mature classifier drops vanilla name",
                !BirthCorrelationPolicy.IsMatureBirthDef("BabyBorn", custom));
            AssertEqual("XML snapshot can replace loss classifier", BiotechFamilyRoleTokens.Birther,
                BirthCorrelationPolicy.MiscarriageRole(
                    "ModdedLoss", "Birther", "Birther", "Mother", "Father", custom));
        }

        private static void TestStableSchemaAndArcKeys()
        {
            AssertEqual("growth event defName", "BiotechGrowthMoment", BiotechEventDefNames.GrowthMoment);
            AssertEqual("birth event defName", "BiotechFamilyBirth", BiotechEventDefNames.FamilyBirth);
            AssertEqual("implant cause token", "xenogerm_implant",
                GeneChangeCauseTokens.XenogermImplant);
            AssertEqual("reimplant cause token", "xenogerm_reimplant",
                GeneChangeCauseTokens.XenogermReimplant);
            AssertEqual("fallback cause token", "observed_change",
                GeneChangeCauseTokens.ObservedChange);
            AssertEqual("pregnancy arc key", "biotech-family|Birther_1|Hediff_7",
                BiotechArcKeys.FamilyFromPregnancy(" Birther_1 ", "Hediff_7"));
            AssertEqual("child arc key", "biotech-family|Child_4", BiotechArcKeys.FamilyFromChild("Child_4"));
            AssertTrue("family arc is not double-prefixed",
                !BiotechArcKeys.FamilyFromChild("Child_4").StartsWith("biotech-family|biotech-family|"));
            AssertEqual("growth correlation", "growth|Child_4|10",
                BiotechArcKeys.GrowthCorrelation("Child_4", 10));
            AssertEqual("blank family id fails closed", string.Empty, BiotechArcKeys.FamilyFromChild(" "));
            AssertEqual("bad pregnancy id fails closed", string.Empty,
                BiotechArcKeys.FamilyFromPregnancy("Birther_1", " "));
            AssertEqual("arc delimiter injection fails closed", string.Empty,
                BiotechArcKeys.FamilyFromChild("Child|Other"));
            AssertEqual("family arcs Scribe key", "biotechFamilyArcs", BiotechSaveKeys.FamilyArcs);
            AssertEqual("pending growth Scribe key", "pendingBiotechGrowthMoments",
                BiotechSaveKeys.PendingGrowthMoments);
            AssertEqual("pending birth Scribe key", "pendingBiotechBirths", BiotechSaveKeys.PendingBirths);
            AssertEqual("nested progression Scribe key", "biotechProgressionState",
                BiotechSaveKeys.PawnProgressionState);
            AssertEqual("nested gene observation Scribe key", "geneIdentityObservationState",
                BiotechSaveKeys.GeneIdentityObservationState);
            AssertEqual("gene observation version Scribe key", "geneObservationVersion",
                BiotechSaveKeys.GeneObservationVersion);
            AssertEqual("gene xenotype Def Scribe key", "geneObservedXenotypeDefName",
                BiotechSaveKeys.GeneObservedXenotypeDefName);
            AssertEqual("gene xenotype label Scribe key", "geneObservedXenotypeLabel",
                BiotechSaveKeys.GeneObservedXenotypeLabel);
            AssertEqual("gene membership Scribe key", "geneObservedDefNames",
                BiotechSaveKeys.GeneObservedDefNames);
            AssertEqual("gene truncation Scribe key", "geneObservedMembershipTruncated",
                BiotechSaveKeys.GeneObservedMembershipTruncated);
        }

        private static void TestGrowthDiffAndTruthBoundaries()
        {
            GrowthPawnSnapshot before = Snapshot("Child_1", 13, 6, "Alex", false,
                new[] { Trait("Kind", "kind", "kind description") },
                new[] { Skill("Shooting", "shooting", "none"), Skill("Plants", "plants", "minor") });
            GrowthPawnSnapshot after = Snapshot("Child_1", 13, 6, "Lex", true,
                new[]
                {
                    Trait("Kind", "kind", "kind description"),
                    Trait("Tough", "tough", "takes pain in stride"),
                    Trait("Bisexual", "bisexual", "automatic age trait")
                },
                new[] { Skill("Shooting", "shooting", "minor"), Skill("Plants", "plants", "major") });
            GrowthMomentMutation mutation = GrowthMomentPolicy.Diff(before, after, new GrowthCommittedChoice
            {
                selectedTraitKey = "Tough",
                selectedPassionSkillDefNames = new List<string> { "Plants", "Shooting", "Crafting" },
                sourceToken = BiotechGrowthSourceTokens.PlayerChoice,
                familyArcId = "biotech-family|Child_1"
            }, BiotechPolicySnapshot.CreateDefault());

            AssertNotNull("verified growth diff", mutation);
            AssertEqual("growth stage", "age_13", mutation.stageToken);
            AssertEqual("selected trait verified", "Tough", mutation.selectedTrait.traitKey);
            AssertSequence("all new traits consumed", new[] { "Bisexual", "Tough" },
                mutation.additionalTraitKeysToConsume);
            AssertEqual("two actual passion increases", 2, mutation.passionChanges.Count);
            AssertEqual("passions deterministic order", "Plants", mutation.passionChanges[0].skillDefName);
            AssertTrue("nickname diff", mutation.nicknameChanged);
            AssertEqual("old nickname", "Alex", mutation.previousShortName);
            AssertEqual("new nickname", "Lex", mutation.currentShortName);
            AssertTrue("new responsibility diff", mutation.newResponsibilities);
            AssertEqual("opportunity mapped, not raw tier", "broad", mutation.opportunityBand);
            AssertEqual("stable family arc copied", "biotech-family|Child_1", mutation.familyArcId);
            AssertEqual("stable dedup", "growth|Child_1|13", mutation.correlationId);

            GrowthCommittedChoice noTraitChoice = new GrowthCommittedChoice
            {
                selectedTraitKey = "NoTrait",
                sourceToken = BiotechGrowthSourceTokens.PlayerChoice
            };
            GrowthMomentMutation noTrait = GrowthMomentPolicy.Diff(
                Snapshot("Child_2", 7, 2, "Bea", false, null, new[] { Skill("Artistic", "art", "none") }),
                Snapshot("Child_2", 7, 2, "Bea", false,
                    new[] { Trait("Patient", "patient", "waits") },
                    new[] { Skill("Artistic", "art", "none") }),
                noTraitChoice,
                BiotechPolicySnapshot.CreateDefault());
            AssertNotNull("additional age trait remains a mutation", noTrait);
            AssertTrue("NoTrait is never exposed", noTrait.selectedTrait == null);
            AssertSequence("additional trait still consumed", new[] { "Patient" },
                noTrait.additionalTraitKeysToConsume);

            GrowthMomentMutation autoOne = GrowthMomentPolicy.Diff(
                Snapshot("Child_3", 10, 5, "Cam", false, null, null),
                Snapshot("Child_3", 10, 5, "Cam", false,
                    new[] { Trait("Brave", "brave", "stands firm") }, null),
                new GrowthCommittedChoice { sourceToken = BiotechGrowthSourceTokens.AutoResolved },
                BiotechPolicySnapshot.CreateDefault());
            AssertEqual("one auto trait is unambiguous", "Brave", autoOne.selectedTrait.traitKey);

            GrowthMomentMutation autoMany = GrowthMomentPolicy.Diff(
                Snapshot("Child_4", 13, 8, "Dee", false, null, null),
                Snapshot("Child_4", 13, 8, "Dee", false,
                    new[] { Trait("Brave", "brave", "stands firm"), Trait("Gay", "gay", "automatic") }, null),
                new GrowthCommittedChoice { sourceToken = BiotechGrowthSourceTokens.AutoResolved },
                BiotechPolicySnapshot.CreateDefault());
            AssertTrue("multiple auto traits stay generic", autoMany.selectedTrait == null);
            AssertEqual("multiple auto traits all consumed", 2, autoMany.additionalTraitKeysToConsume.Count);

            GrowthMomentMutation unselectedPassion = GrowthMomentPolicy.Diff(
                Snapshot("Child_5", 7, 3, "Eli", false, null,
                    new[] { Skill("Shooting", "shooting", "none") }),
                Snapshot("Child_5", 7, 3, "Eli", false, null,
                    new[] { Skill("Shooting", "shooting", "minor") }),
                new GrowthCommittedChoice
                {
                    sourceToken = BiotechGrowthSourceTokens.PlayerChoice,
                    selectedPassionSkillDefNames = new List<string> { "Plants" }
                },
                BiotechPolicySnapshot.CreateDefault());
            AssertTrue("unselected passion change is not claimed", unselectedPassion == null);

            AssertTrue("no mutation returns empty", GrowthMomentPolicy.Diff(before, before,
                new GrowthCommittedChoice { sourceToken = BiotechGrowthSourceTokens.PlayerChoice },
                BiotechPolicySnapshot.CreateDefault()) == null);
            AssertTrue("mismatched pawn returns empty", GrowthMomentPolicy.Diff(before,
                Snapshot("Other", 13, 6, "Alex", false, null, null),
                new GrowthCommittedChoice { sourceToken = BiotechGrowthSourceTokens.PlayerChoice },
                BiotechPolicySnapshot.CreateDefault()) == null);
            AssertTrue("non-growth age returns empty", GrowthMomentPolicy.Diff(
                Snapshot("Child_6", 8, 4, "Fox", false, null, null),
                Snapshot("Child_6", 8, 4, "Fox", true, null, null),
                new GrowthCommittedChoice { sourceToken = BiotechGrowthSourceTokens.PlayerChoice },
                BiotechPolicySnapshot.CreateDefault()) == null);
        }

        private static void TestOpportunityBands()
        {
            BiotechPolicySnapshot policy = BiotechPolicySnapshot.CreateDefault();
            string[] expected = { "narrow", "narrow", "narrow", "mixed", "mixed", "mixed", "broad", "broad", "exceptional" };
            for (int tier = 0; tier <= 8; tier++)
            {
                AssertEqual("opportunity tier " + tier, expected[tier],
                    GrowthMomentPolicy.OpportunityBandFor(tier, policy).token);
            }

            BiotechPolicySnapshot reordered = BiotechPolicySnapshot.CreateDefault();
            reordered.opportunityBands.Reverse();
            AssertEqual("band mapping does not depend on XML row order", "mixed",
                GrowthMomentPolicy.OpportunityBandFor(4, reordered).token);
            AssertEqual("missing policy uses code token fallback", "exceptional",
                GrowthMomentPolicy.OpportunityBandFor(99, null).token);
            AssertEqual("code fallback contains no hardcoded prompt prose", string.Empty,
                GrowthMomentPolicy.OpportunityBandFor(99, null).description);
        }

        private static void TestPendingGrowthNormalization()
        {
            PendingBiotechGrowthMoment older = Pending("Child_B", 10, 100, 110);
            PendingBiotechGrowthMoment newer = Pending("Child_B", 10, 200, 210);
            newer.correlationId = "corrupt";
            newer.birthdaySnapshot.traits = new List<GrowthTraitFact>
            {
                Trait("Tough", "tough", "steady"),
                Trait("Tough", "duplicate", "duplicate")
            };
            newer.birthdaySnapshot.skills = null;
            PendingBiotechGrowthMoment future = Pending("Child_A", 7, 9999, 9999);
            PendingBiotechGrowthMoment invalidAge = Pending("Child_C", 8, 50, 60);
            PendingBiotechGrowthMoment invalidId = Pending("Child|Injected", 13, 50, 60);

            List<PendingBiotechGrowthMoment> normalized =
                PendingBiotechGrowthMomentPolicy.Normalize(
                    new[] { null, older, newer, future, invalidAge, invalidId },
                    currentTick: 500);
            AssertEqual("pending normalization keeps two valid pawn/age rows", 2, normalized.Count);
            AssertEqual("pending normalization stable sort", "Child_A", normalized[0].pawnId);
            AssertEqual("future birthday tick clamped", 500, normalized[0].birthdayTick);
            AssertEqual("future configured tick clamped", 500, normalized[0].configuredTick);
            AssertEqual("duplicate pawn/age keeps newest", 200, normalized[1].birthdayTick);
            AssertEqual("correlation repaired", "growth|Child_B|10", normalized[1].correlationId);
            AssertEqual("duplicate trait rows collapsed", 1, normalized[1].birthdaySnapshot.traits.Count);
            AssertNotNull("null skill list repaired", normalized[1].birthdaySnapshot.skills);
            AssertEqual("find newest after normalization", newer,
                PendingBiotechGrowthMomentPolicy.FindNewest(normalized, "Child_B", 10));
            AssertTrue("missing correlation returns null",
                PendingBiotechGrowthMomentPolicy.FindNewest(normalized, "Missing", 10) == null);
            AssertTrue("pending row not expired before XML boundary",
                !PendingBiotechGrowthMomentPolicy.IsExpired(newer, 1199, 1000));
            AssertTrue("pending row expires at elapsed boundary",
                PendingBiotechGrowthMomentPolicy.IsExpired(newer, 1200, 1000));
            AssertTrue("fallback grace is inclusive",
                PendingBiotechGrowthMomentPolicy.IsPastFallbackGrace(newer, 410, 200));
            AssertTrue("fallback waits before grace",
                !PendingBiotechGrowthMomentPolicy.IsPastFallbackGrace(newer, 409, 200));
            AssertTrue("negative grace is disabled",
                !PendingBiotechGrowthMomentPolicy.IsPastFallbackGrace(newer, 9999, -1));

            List<PendingBiotechGrowthMoment> capped = PendingBiotechGrowthMomentPolicy.Normalize(
                new[]
                {
                    Pending("OldGrowth", 7, 100, 110),
                    Pending("MiddleGrowth", 7, 200, 210),
                    Pending("NewGrowth", 7, 300, 310)
                },
                currentTick: 500,
                maximumRows: 2);
            AssertEqual("pending growth rows respect configured cap", 2, capped.Count);
            AssertTrue("pending growth cap keeps newest ownership",
                capped.Any(row => row.pawnId == "MiddleGrowth")
                && capped.Any(row => row.pawnId == "NewGrowth")
                && !capped.Any(row => row.pawnId == "OldGrowth"));

            List<PendingBiotechGrowthMoment> exactGrowthCap = PendingBiotechGrowthMomentPolicy.Normalize(
                new[]
                {
                    Pending("ExactGrowthA", 7, 100, 110),
                    Pending("ExactGrowthB", 7, 200, 210)
                },
                currentTick: 500,
                maximumRows: 2);
            AssertEqual("pending growth exact-cap boundary keeps every row", 2, exactGrowthCap.Count);

            List<PendingBiotechGrowthMoment> manyGrowthRows = new List<PendingBiotechGrowthMoment>();
            for (int i = 0; i < 300; i++)
            {
                manyGrowthRows.Add(Pending("GrowthCap_" + i, 7, i + 1, i + 1));
            }

            AssertEqual("hard ceiling preserves a pre-cap save above the XML default", 300,
                PendingBiotechGrowthMomentPolicy.Normalize(
                    manyGrowthRows,
                    currentTick: 500,
                    maximumRows: BiotechPendingOwnershipLimits.HardMaximumRows).Count);
            foreach (int invalidCap in new[] { 0, -1, 9999 })
            {
                List<PendingBiotechGrowthMoment> repairedCap =
                    PendingBiotechGrowthMomentPolicy.Normalize(
                        manyGrowthRows,
                        currentTick: 500,
                        maximumRows: invalidCap);
                AssertEqual("invalid pending growth cap uses shipped default " + invalidCap,
                    BiotechPendingOwnershipLimits.DefaultMaximumRows,
                    repairedCap.Count);
                AssertTrue("invalid pending growth cap keeps newest rows " + invalidCap,
                    repairedCap.Any(row => row.pawnId == "GrowthCap_299")
                    && !repairedCap.Any(row => row.pawnId == "GrowthCap_0"));
            }

            AssertTrue("growth admission accepts the final free slot",
                BiotechPendingOwnershipLimits.CanAdmit(existingRows: 1, requestedMaximumRows: 2));
            AssertTrue("growth admission rejects a newcomer instead of evicting claimed ownership",
                !BiotechPendingOwnershipLimits.CanAdmit(existingRows: 2, requestedMaximumRows: 2));
        }

        private static void TestFamilyArcObservationAndRetention()
        {
            BiotechPolicySnapshot policy = BiotechPolicySnapshot.CreateDefault();
            policy.familyActivityExactDefNames.Add("BabyPlay");
            policy.familyActivityPrefixes.Add("Lesson");
            policy.familyPregnancyHediffDefNames.Add("PregnantHuman");
            policy.familyLaborHediffDefNames.Add("PregnancyLabor");
            policy.familyLessonAdultThoughtDefNames.Add("GaveLesson");
            policy.familyLessonChildThoughtDefNames.Add("WasTaught");
            AssertEqual("BabyPlay exact activity", BiotechFamilyActivityKindTokens.BabyPlay,
                FamilyArcPolicy.ClassifyInteraction("BabyPlay", policy));
            AssertEqual("pregnancy Hediff exact activity", BiotechFamilyHediffKindTokens.Pregnancy,
                FamilyArcPolicy.ClassifyFamilyHediff("PregnantHuman", policy));
            AssertEqual("labor Hediff exact activity", BiotechFamilyHediffKindTokens.Labor,
                FamilyArcPolicy.ClassifyFamilyHediff("PregnancyLabor", policy));
            AssertEqual("Lesson prefix activity", BiotechFamilyActivityKindTokens.Lesson,
                FamilyArcPolicy.ClassifyInteraction("Lesson_Social", policy));
            AssertEqual("unrelated interaction ignored", string.Empty,
                FamilyArcPolicy.ClassifyInteraction("Chitchat", policy));
            AssertEqual("adult memory orientation", BiotechFamilyMemoryKindTokens.AdultRememberedLesson,
                FamilyArcPolicy.ClassifyLessonMemory("GaveLesson", policy));
            AssertEqual("child memory orientation", BiotechFamilyMemoryKindTokens.ChildRememberedLesson,
                FamilyArcPolicy.ClassifyLessonMemory("WasTaught", policy));

            List<BiotechFamilyArcState> arcs = new List<BiotechFamilyArcState>();
            BiotechFamilyArcState pregnancy = FamilyArcPolicy.ObserveFamilyHediff(arcs,
                new FamilyHediffSnapshot
                {
                    kindToken = BiotechFamilyHediffKindTokens.Pregnancy,
                    hediffId = "Hediff_1",
                    birtherId = "Birther",
                    birtherName = "Ari",
                    geneticMotherId = "Mother",
                    fatherId = "Father",
                    observedTick = 100
                }, 12);
            AssertEqual("pregnancy opens stable arc", "biotech-family|Birther|Hediff_1",
                pregnancy.familyArcId);
            BiotechFamilyArcState labor = FamilyArcPolicy.ObserveFamilyHediff(arcs,
                new FamilyHediffSnapshot
                {
                    kindToken = BiotechFamilyHediffKindTokens.Labor,
                    hediffId = "Hediff_2",
                    birtherId = "Birther",
                    geneticMotherId = "Mother",
                    fatherId = "Father",
                    observedTick = 200
                }, 12);
            AssertTrue("labor correlates to pregnancy object", object.ReferenceEquals(pregnancy, labor));
            AssertEqual("labor ID appended", "Hediff_2", pregnancy.laborHediffId);
            BiotechFamilyArcState pushing = FamilyArcPolicy.ObserveFamilyHediff(arcs,
                new FamilyHediffSnapshot
                {
                    kindToken = BiotechFamilyHediffKindTokens.Labor,
                    hediffId = "Hediff_3",
                    birtherId = "Birther",
                    geneticMotherId = "Mother",
                    fatherId = "Father",
                    observedTick = 210
                }, 12);
            AssertTrue("labor-pushing transition reuses pregnancy arc",
                object.ReferenceEquals(pregnancy, pushing));
            AssertEqual("pushing replaces active labor ID", "Hediff_3", pregnancy.laborHediffId);
            AssertEqual("pregnancy labor lifecycle stays one arc", 1, arcs.Count);
            AssertEqual("live unresolved pregnancy remains retained", FamilyArcRetentionAction.Keep,
                FamilyArcPolicy.DecideRetention(
                    pregnancy,
                    new FamilyArcRetentionInput { familyHediffStillPresent = true },
                    5000,
                    1000));
            AssertEqual("ended unresolved pregnancy compacts after grace", FamilyArcRetentionAction.Compact,
                FamilyArcPolicy.DecideRetention(
                    pregnancy,
                    new FamilyArcRetentionInput(),
                    5000,
                    1000));
            FamilyArcPolicy.Compact(pregnancy);
            AssertEqual("ended compacted pregnancy removes on next pass", FamilyArcRetentionAction.Remove,
                FamilyArcPolicy.DecideRetention(
                    pregnancy,
                    new FamilyArcRetentionInput(),
                    5000,
                    1000));
            BiotechFamilyArcState laborOnly = FamilyArcPolicy.ObserveFamilyHediff(
                new List<BiotechFamilyArcState>(),
                new FamilyHediffSnapshot
                {
                    kindToken = BiotechFamilyHediffKindTokens.Labor,
                    hediffId = "LaborOnly",
                    birtherId = "LoadedBirther",
                    observedTick = 200
                }, 12);
            AssertEqual("labor-first baseline survives normalization", 1,
                FamilyArcPolicy.Normalize(
                    new List<BiotechFamilyArcState> { laborOnly }, 300, 12).Count);

            BiotechFamilyArcState childArc = FamilyArcPolicy.EnsureChildArc(arcs,
                new FamilyChildSnapshot
                {
                    childId = "Child",
                    childName = "Bea",
                    observedTick = 300,
                    parents = new List<FamilyParticipantFact>
                    {
                        new FamilyParticipantFact
                        {
                            pawnId = "Parent",
                            displayName = "Cam",
                            roleToken = BiotechFamilyRoleTokens.Parent
                        },
                        new FamilyParticipantFact
                        {
                            pawnId = "BirthParent",
                            displayName = "Dee",
                            roleToken = BiotechFamilyRoleTokens.BirthParent
                        }
                    }
                }, 12);
            AssertEqual("child baseline key", "biotech-family|Child", childArc.familyArcId);
            AssertEqual("exact parent rows kept", 2, childArc.supporters.Count);
            AssertTrue("zero-count current parent rows provide exact baseline continuity",
                FamilyArcPolicy.HasExactFamilyConnection(childArc));
            AssertTrue("zero-count current parent rows do not invent observed upbringing",
                !FamilyArcPolicy.HasObservedUpbringing(childArc));

            BiotechFamilyArcState childOnlyArc = FamilyArcPolicy.EnsureChildArc(
                new List<BiotechFamilyArcState>(),
                new FamilyChildSnapshot
                {
                    childId = "ChildOnly",
                    childName = "Finn",
                    observedTick = 300
                }, 12);
            FamilyArcPolicy.MarkGrowthSummarized(childOnlyArc, 7, 500);
            AssertTrue("prior growth alone does not invent observed upbringing",
                !FamilyArcPolicy.HasObservedUpbringing(childOnlyArc));
            AssertTrue("child-only arc has no exact family baseline",
                !FamilyArcPolicy.HasExactFamilyConnection(childOnlyArc));

            FamilyArcPolicy.ObserveActivity(arcs, new FamilyActivityObservation
            {
                kindToken = BiotechFamilyActivityKindTokens.Lesson,
                adultId = "Teacher",
                adultName = "Eli",
                childId = "Child",
                childName = "Bea",
                observedTick = 400
            }, 12);
            FamilySupportObservationState teacher = childArc.supporters.Single(row => row.adultId == "Teacher");
            AssertEqual("lesson count recorded", 1, teacher.lessonCount);
            AssertTrue("exact lesson supplies observed upbringing continuity",
                FamilyArcPolicy.HasObservedUpbringing(childArc));
            AssertEqual("lesson is initially unsummarized", 1,
                FamilyArcPolicy.UnsummarizedEvidence(teacher));
            FamilyArcPolicy.MarkGrowthSummarized(childArc, 7, 500);
            AssertEqual("summarized lesson no longer repeats", 0,
                FamilyArcPolicy.UnsummarizedEvidence(teacher));
            FamilyArcPolicy.ObserveActivity(arcs, new FamilyActivityObservation
            {
                kindToken = BiotechFamilyActivityKindTokens.BabyPlay,
                adultId = "Teacher",
                adultName = "Eli",
                childId = "Child",
                observedTick = 600
            }, 12);
            AssertEqual("new play is next-growth evidence", 1,
                FamilyArcPolicy.UnsummarizedEvidence(teacher));
            AssertSequence("growth ages are stable and unique", new[] { 7 }, childArc.recordedGrowthAges);

            List<BiotechFamilyArcState> normalized = FamilyArcPolicy.Normalize(
                new List<BiotechFamilyArcState>
                {
                    childArc,
                    new BiotechFamilyArcState
                    {
                        familyArcId = childArc.familyArcId,
                        childId = "Child",
                        supporters = new List<FamilySupportObservationState>
                        {
                            new FamilySupportObservationState { adultId = "Teacher", lessonCount = 2 }
                        }
                    }
                }, 700, 12);
            AssertEqual("duplicate family arcs merge", 1, normalized.Count);
            AssertEqual("duplicate supporter lifetime counts merge", 3,
                normalized[0].supporters.Single(row => row.adultId == "Teacher").lessonCount);

            BiotechFamilyArcState retained = normalized[0];
            FamilyArcPolicy.MarkGrowthSummarized(retained, 10, 700);
            AssertEqual("young living child retains arc", FamilyArcRetentionAction.Keep,
                FamilyArcPolicy.DecideRetention(retained,
                    new FamilyArcRetentionInput { childAliveAndDeveloping = true }, 5000, 1000));
            AssertEqual("elapsed settled arc compacts first", FamilyArcRetentionAction.Compact,
                FamilyArcPolicy.DecideRetention(retained, new FamilyArcRetentionInput(), 5000, 1000));
            FamilyArcPolicy.Compact(retained);
            AssertTrue("compaction marker saved", retained.detailsCompacted);
            AssertEqual("compacted unreferenced arc removes next pass", FamilyArcRetentionAction.Remove,
                FamilyArcPolicy.DecideRetention(retained, new FamilyArcRetentionInput(), 5000, 1000));
            AssertEqual("saved event reference always retains compacted arc", FamilyArcRetentionAction.Keep,
                FamilyArcPolicy.DecideRetention(retained,
                    new FamilyArcRetentionInput { hasSavedEventReference = true }, 5000, 1000));

            // Unsummarized supporter evidence is consumable only by a future growth page, so it keeps
            // an arc only through the childAliveAndDeveloping flag. A child who died (or grew up) with
            // recorded lessons must NOT be immortal: the arc follows the ordinary retention countdown.
            BiotechFamilyArcState orphanEvidence = new BiotechFamilyArcState
            {
                familyArcId = "biotech-family|Gone",
                childId = "Gone",
                lastObservedTick = 100,
                supporters = new List<FamilySupportObservationState>
                {
                    new FamilySupportObservationState { adultId = "Teacher", lessonCount = 3 }
                }
            };
            AssertEqual("live developing child keeps unconsumed lesson evidence", FamilyArcRetentionAction.Keep,
                FamilyArcPolicy.DecideRetention(orphanEvidence,
                    new FamilyArcRetentionInput { childAliveAndDeveloping = true }, 5000, 1000));
            AssertEqual("dead or grown child with unconsumable evidence is not immortal",
                FamilyArcRetentionAction.Compact,
                FamilyArcPolicy.DecideRetention(orphanEvidence, new FamilyArcRetentionInput(), 5000, 1000));
        }

        private static void TestSupporterSelectionAndWriterShapes()
        {
            BiotechPolicySnapshot policy = BiotechPolicySnapshot.CreateDefault();
            List<FamilySupportCandidate> candidates = new List<FamilySupportCandidate>
            {
                Candidate("Teacher", string.Empty, 12, 200, true, true),
                Candidate("Parent", BiotechFamilyRoleTokens.Parent, 0, 100, true, false)
            };
            FamilySupportSelection parent = FamilySupportPolicy.Select(candidates, policy);
            AssertEqual("exact parent outranks active teacher tier", "Parent", parent.adultId);
            AssertEqual("parent role preserved", BiotechFamilyRoleTokens.Parent, parent.roleToken);

            candidates = new List<FamilySupportCandidate>
            {
                Candidate("ParentA", BiotechFamilyRoleTokens.Parent, 1, 300, true, true),
                Candidate("ParentB", BiotechFamilyRoleTokens.BirthParent, 8, 200, true, false)
            };
            FamilySupportSelection engaged = FamilySupportPolicy.Select(candidates, policy);
            AssertEqual("higher observation band wins within parent tier", "ParentB", engaged.adultId);
            AssertEqual("deep observation band", "deep", engaged.observationBand);

            candidates = new List<FamilySupportCandidate>
            {
                Candidate("SameAdult", string.Empty, 8, 400, true, true),
                Candidate("SameAdult", BiotechFamilyRoleTokens.Parent, 0, 100, true, false)
            };
            AssertEqual("duplicate adult rows choose best role independent of first occurrence",
                BiotechFamilyRoleTokens.Parent, FamilySupportPolicy.Select(candidates, policy).roleToken);

            candidates = new List<FamilySupportCandidate>
            {
                Candidate("B", string.Empty, 4, 100, true, true),
                Candidate("A", string.Empty, 4, 100, true, true)
            };
            AssertEqual("stable ID tie-break", "A", FamilySupportPolicy.Select(candidates, policy).adultId);
            AssertTrue("insufficient non-parent evidence omitted",
                FamilySupportPolicy.Select(new List<FamilySupportCandidate>
                {
                    Candidate("Adult", string.Empty, 1, 100, true, true)
                }, policy) == null);
            AssertTrue("ineligible parent omitted",
                FamilySupportPolicy.Select(new List<FamilySupportCandidate>
                {
                    Candidate("Parent", BiotechFamilyRoleTokens.Parent, 10, 100, false, true)
                }, policy) == null);

            FamilySupportSelection supporter = new FamilySupportSelection { adultId = "Adult" };
            AssertEqual("child/supporter pair", GrowthWriterShape.Pair,
                GrowthWriterPolicy.Decide("Child", true, supporter));
            AssertEqual("child solo", GrowthWriterShape.ChildSolo,
                GrowthWriterPolicy.Decide("Child", true, null));
            AssertEqual("supporter solo", GrowthWriterShape.SupporterSolo,
                GrowthWriterPolicy.Decide("Child", false, supporter));
            AssertEqual("no writer", GrowthWriterShape.Drop,
                GrowthWriterPolicy.Decide("Child", false, null));
            AssertEqual("self-supporter cannot create pair", GrowthWriterShape.ChildSolo,
                GrowthWriterPolicy.Decide("Child", true, new FamilySupportSelection { adultId = "Child" }));
        }

        private static void TestBirthWriterSelection()
        {
            AssertEqual("live positive birth outcome", BiotechBirthOutcomeTokens.Healthy,
                BiotechBirthOutcomeTokens.FromCanonicalResult(false, 1));
            AssertEqual("live sick birth outcome", BiotechBirthOutcomeTokens.InfantIllness,
                BiotechBirthOutcomeTokens.FromCanonicalResult(false, 0));
            AssertEqual("corpse birth outcome", BiotechBirthOutcomeTokens.Stillbirth,
                BiotechBirthOutcomeTokens.FromCanonicalResult(true, -1));
            AssertEqual("invalid negative live outcome rejected", string.Empty,
                BiotechBirthOutcomeTokens.FromCanonicalResult(false, -1));
            AssertEqual("distinct birther and mother is surrogacy", BiotechBirthMethodTokens.Surrogacy,
                BiotechBirthMethodTokens.FromCanonicalParticipants(false, "Birther", "Mother"));
            AssertEqual("same birther and mother is pregnancy", BiotechBirthMethodTokens.Pregnancy,
                BiotechBirthMethodTokens.FromCanonicalParticipants(false, "Mother", "Mother"));
            AssertEqual("exact vat route wins over participants", BiotechBirthMethodTokens.GrowthVat,
                BiotechBirthMethodTokens.FromCanonicalParticipants(true, "Birther", "Mother"));

            BirthMutationSnapshot snapshot = new BirthMutationSnapshot
            {
                birther = Participant("Mom", "Birther", true),
                geneticMother = Participant("Mom", "Mother", true),
                father = Participant("Dad", "Father", true)
            };
            BirthWriterSelection writers = BirthOwnershipPolicy.SelectWriters(snapshot,
                BiotechPolicySnapshot.CreateDefault());
            AssertEqual("birth writer cap", 2, writers.writers.Count);
            AssertEqual("birther first", "Mom", writers.writers[0].pawnId);
            AssertEqual("birther role certainty", BiotechFamilyRoleTokens.Birther, writers.writers[0].roleToken);
            AssertEqual("duplicate mother skipped for father", "Dad", writers.writers[1].pawnId);
            AssertEqual("father role", BiotechFamilyRoleTokens.Father, writers.writers[1].roleToken);

            snapshot.birther.eligible = false;
            writers = BirthOwnershipPolicy.SelectWriters(snapshot, BiotechPolicySnapshot.CreateDefault());
            AssertEqual("eligible genetic mother first without birther", BiotechFamilyRoleTokens.GeneticMother,
                writers.writers[0].roleToken);

            BiotechPolicySnapshot oneWriter = BiotechPolicySnapshot.CreateDefault();
            oneWriter.maximumBirthWriters = 1;
            writers = BirthOwnershipPolicy.SelectWriters(snapshot, oneWriter);
            AssertEqual("XML one-writer cap", 1, writers.writers.Count);

            BiotechPolicySnapshot malformed = BiotechPolicySnapshot.CreateDefault();
            malformed.maximumBirthWriters = 99;
            writers = BirthOwnershipPolicy.SelectWriters(snapshot, malformed);
            AssertEqual("malformed writer cap uses safe fallback", 2, writers.writers.Count);
            AssertEqual("null birth has no writers", 0,
                BirthOwnershipPolicy.SelectWriters(null, null).writers.Count);
        }

        private static void TestBirthArcAndPendingOwnership()
        {
            BiotechPolicySnapshot policy = BiotechPolicySnapshot.CreateDefault();
            List<BiotechFamilyArcState> arcs = new List<BiotechFamilyArcState>();
            FamilyArcPolicy.ObserveFamilyHediff(arcs, new FamilyHediffSnapshot
            {
                kindToken = BiotechFamilyHediffKindTokens.Pregnancy,
                hediffId = "Pregnancy_7",
                birtherId = "Birther",
                birtherName = "Ari",
                geneticMotherId = "Mother",
                geneticMotherName = "Bo",
                fatherId = "Father",
                fatherName = "Cy",
                observedTick = 100
            }, policy.maximumSupporterRows);

            BirthMutationSnapshot birth = BirthSnapshot("Child", 200);
            bool alreadyAttached;
            BiotechFamilyArcState attached = FamilyArcPolicy.AttachBirth(
                arcs,
                birth,
                policy.maximumSupporterRows,
                out alreadyAttached);
            AssertTrue("first birth attachment is new", !alreadyAttached);
            AssertEqual("birth keeps pregnancy arc", "biotech-family|Birther|Pregnancy_7", attached.familyArcId);
            AssertEqual("snapshot receives stable family arc", attached.familyArcId, birth.familyArcId);
            AssertEqual("birth child attached", "Child", attached.childId);
            AssertEqual("birth exact outcome saved", BiotechBirthOutcomeTokens.Healthy,
                attached.birthOutcomeToken);
            AssertTrue("birth closes unresolved pregnancy", attached.closed && !attached.baselineOnly);
            AssertTrue("birth participants become exact parent rows",
                attached.supporters.Exists(row => row.adultId == "Birther"
                    && row.relationToken == BiotechFamilyRoleTokens.BirthParent)
                && attached.supporters.Exists(row => row.adultId == "Mother"
                    && row.relationToken == BiotechFamilyRoleTokens.Parent));

            BirthMutationSnapshot twinBirth = BirthSnapshot("Twin", 200);
            BiotechFamilyArcState twin = FamilyArcPolicy.AttachBirth(
                arcs,
                twinBirth,
                policy.maximumSupporterRows,
                out alreadyAttached);
            AssertTrue("twin receives its own child-keyed arc", !alreadyAttached
                && twin.familyArcId == "biotech-family|Twin"
                && twin.familyArcId != attached.familyArcId);
            AssertEqual("twin retains pregnancy evidence", attached.pregnancyHediffId,
                twin.pregnancyHediffId);
            AssertTrue("twin clones family supporters without sharing mutable rows",
                twin.supporters.Count == attached.supporters.Count
                && !object.ReferenceEquals(twin.supporters[0], attached.supporters[0]));

            BirthMutationSnapshot repeated = BirthSnapshot("Child", 250);
            repeated.outcomeToken = BiotechBirthOutcomeTokens.Stillbirth;
            repeated.methodToken = BiotechBirthMethodTokens.GrowthVat;
            FamilyArcPolicy.AttachBirth(arcs, repeated, policy.maximumSupporterRows, out alreadyAttached);
            AssertTrue("same child birth attachment is recognized", alreadyAttached);
            AssertEqual("repeated birth cannot rewrite exact outcome", BiotechBirthOutcomeTokens.Healthy,
                attached.birthOutcomeToken);
            AssertEqual("repeated birth cannot rewrite exact method", BiotechBirthMethodTokens.Surrogacy,
                attached.birthMethodToken);

            List<BiotechFamilyArcState> ambiguous = new List<BiotechFamilyArcState>
            {
                OpenBirthArc("biotech-family|Birther|A", "A", 50),
                OpenBirthArc("biotech-family|Birther|B", "B", 50)
            };
            BirthMutationSnapshot ambiguousBirth = BirthSnapshot("OtherChild", 70);
            BiotechFamilyArcState fallback = FamilyArcPolicy.AttachBirth(
                ambiguous,
                ambiguousBirth,
                policy.maximumSupporterRows,
                out alreadyAttached);
            AssertEqual("ambiguous pregnancies use child-key fallback",
                "biotech-family|OtherChild", fallback.familyArcId);

            BiotechFamilyArcState exactParents = OpenBirthArc(
                "biotech-family|Birther|Exact", "Exact", 40);
            BiotechFamilyArcState unknownParents = OpenBirthArc(
                "biotech-family|Birther|UnknownParents", "UnknownParents", 60);
            unknownParents.geneticMotherId = string.Empty;
            unknownParents.fatherId = string.Empty;
            BirthMutationSnapshot exactParentBirth = BirthSnapshot("ExactChild", 80);
            BiotechFamilyArcState exactMatch = FamilyArcPolicy.AttachBirth(
                new List<BiotechFamilyArcState> { exactParents, unknownParents },
                exactParentBirth,
                policy.maximumSupporterRows,
                out alreadyAttached);
            AssertEqual("known parents select the exact matching arc", exactParents.familyArcId,
                exactMatch.familyArcId);

            BiotechFamilyArcState strictWildcard = OpenBirthArc(
                "biotech-family|Birther|StrictWildcard", "StrictWildcard", 90);
            strictWildcard.geneticMotherId = string.Empty;
            strictWildcard.fatherId = string.Empty;
            BirthMutationSnapshot strictBirth = BirthSnapshot("StrictChild", 100);
            BiotechFamilyArcState strictFallback = FamilyArcPolicy.AttachBirth(
                new List<BiotechFamilyArcState> { strictWildcard },
                strictBirth,
                policy.maximumSupporterRows,
                out alreadyAttached);
            AssertEqual("known incoming parents reject blank-parent wildcard arcs",
                "biotech-family|StrictChild", strictFallback.familyArcId);

            BiotechFamilyArcState loss = OpenBirthArc("biotech-family|Birther|Loss", "Loss", 10);
            FamilyArcPolicy.ClosePregnancyLoss(loss, 20);
            AssertTrue("miscarriage closes exact arc", loss.closed);
            AssertEqual("miscarriage stores exact loss token", BiotechFamilyEndTokens.Loss,
                loss.birthOutcomeToken);
            BiotechFamilyArcState unknown = OpenBirthArc("biotech-family|Birther|Unknown", "Unknown", 10);
            FamilyArcPolicy.CloseUnknown(unknown, 30);
            AssertEqual("disappearance stays semantically unknown", BiotechFamilyEndTokens.EndedUnknown,
                unknown.birthOutcomeToken);

            BiotechFamilyArcState malformedBirth = new BiotechFamilyArcState
            {
                familyArcId = "biotech-family|MalformedChild",
                childId = "MalformedChild",
                birthTick = 15,
                birthOutcomeToken = "guessed_happy",
                birthMethodToken = "teleported"
            };
            BiotechFamilyArcState repairedBirth = FamilyArcPolicy.Normalize(
                new List<BiotechFamilyArcState> { malformedBirth }, 20, 12)[0];
            AssertEqual("malformed saved birth outcome clears", string.Empty,
                repairedBirth.birthOutcomeToken);
            AssertEqual("malformed saved birth method clears", string.Empty,
                repairedBirth.birthMethodToken);

            PendingBiotechBirthState pending = new PendingBiotechBirthState
            {
                snapshot = birth,
                writers = new BirthWriterSelection
                {
                    writers = new List<BirthWriterFact>
                    {
                        new BirthWriterFact
                        {
                            pawnId = "Birther",
                            displayName = "Ari",
                            roleToken = BiotechFamilyRoleTokens.Birther
                        },
                        new BirthWriterFact
                        {
                            pawnId = "Birther",
                            displayName = "duplicate",
                            roleToken = BiotechFamilyRoleTokens.GeneticMother
                        },
                        new BirthWriterFact
                        {
                            pawnId = "Father",
                            displayName = "Cy",
                            roleToken = BiotechFamilyRoleTokens.Father
                        }
                    }
                },
                eventContext = new BirthEventContextSnapshot
                {
                    birthTick = 999,
                    birthDate = "birth date",
                    writers = new List<BirthWriterContextSnapshot>
                    {
                        new BirthWriterContextSnapshot
                        {
                            pawnId = "Birther",
                            displayName = "Ari at birth",
                            pawnSummary = "birth summary",
                            continuity = "birth continuity",
                            pairContinuity = "birth pair continuity",
                            staggeredIntensity = 99
                        },
                        new BirthWriterContextSnapshot { pawnId = "Stranger" }
                    }
                },
                createdTick = 200
            };
            List<PendingBiotechBirthState> normalized = PendingBiotechBirthPolicy.Normalize(
                new List<PendingBiotechBirthState> { null, pending }, 250);
            AssertEqual("one valid pending birth remains", 1, normalized.Count);
            AssertEqual("pending writers are distinct and capped", 2, normalized[0].writers.writers.Count);
            AssertEqual("pending context tick follows canonical snapshot", birth.birthTick,
                normalized[0].eventContext.birthTick);
            AssertEqual("pending context retains only frozen writers", 1,
                normalized[0].eventContext.writers.Count);
            AssertEqual("pending handwriting intensity is clamped", 4,
                normalized[0].eventContext.writers[0].staggeredIntensity);
            AssertEqual("pending context keeps event-time continuity", "birth pair continuity",
                normalized[0].eventContext.writers[0].pairContinuity);

            BirthMutationSnapshot loadBeforeTickRestore = BirthSnapshot("LoadChild", 500);
            loadBeforeTickRestore.familyArcId = "biotech-family|LoadChild";
            List<PendingBiotechBirthState> tickZeroLoad = PendingBiotechBirthPolicy.Normalize(
                new List<PendingBiotechBirthState>
                {
                    new PendingBiotechBirthState
                    {
                        snapshot = loadBeforeTickRestore,
                        writers = new BirthWriterSelection
                        {
                            writers = new List<BirthWriterFact>
                            {
                                new BirthWriterFact
                                {
                                    pawnId = "Birther",
                                    roleToken = BiotechFamilyRoleTokens.Birther
                                }
                            }
                        },
                        createdTick = 500
                    }
                },
                0);
            AssertEqual("tick-zero load does not discard a valid future-looking pending row", 1,
                tickZeroLoad.Count);
            AssertTrue("accepted naming flushes",
                PendingBiotechBirthPolicy.ShouldFlush(pending, new BirthChildNamingState
                {
                    found = true,
                    namingDeadline = -1
                }, 210, 60));
            AssertTrue("unexpired live naming waits",
                !PendingBiotechBirthPolicy.ShouldFlush(pending, new BirthChildNamingState
                {
                    found = true,
                    namingDeadline = 300,
                    dead = false
                }, 250, 60));
            AssertTrue("elapsed naming deadline flushes",
                PendingBiotechBirthPolicy.ShouldFlush(pending, new BirthChildNamingState
                {
                    found = true,
                    namingDeadline = 240
                }, 250, 60));
            AssertTrue("missing child waits through grace",
                !PendingBiotechBirthPolicy.ShouldFlush(pending, new BirthChildNamingState(), 250, 60));
            AssertTrue("missing child flushes after grace",
                PendingBiotechBirthPolicy.ShouldFlush(pending, new BirthChildNamingState(), 260, 60));
            AssertTrue("remaining writer loss flushes before naming",
                PendingBiotechBirthPolicy.ShouldFlush(
                    pending,
                    new BirthChildNamingState
                    {
                        found = true,
                        namingDeadline = 900,
                        dead = false
                    },
                    250,
                    60,
                    currentWriterCount: 1));
            AssertTrue("all writers missing waits through recovery",
                !PendingBiotechBirthPolicy.ShouldFlush(
                    pending,
                    new BirthChildNamingState
                    {
                        found = true,
                        namingDeadline = 900,
                        dead = false
                    },
                    250,
                    60,
                    currentWriterCount: 0));
            AssertTrue("all writers missing wakes discard after recovery",
                PendingBiotechBirthPolicy.ShouldFlush(
                    pending,
                    new BirthChildNamingState
                    {
                        found = true,
                        namingDeadline = 900,
                        dead = false
                    },
                    260,
                    60,
                    currentWriterCount: 0));

            List<PendingBiotechBirthState> cappedBirths = PendingBiotechBirthPolicy.Normalize(
                new List<PendingBiotechBirthState>
                {
                    PendingBirth("OldBirth", 100),
                    PendingBirth("MiddleBirth", 200),
                    PendingBirth("NewBirth", 300)
                },
                currentTick: 500,
                maximumRows: 2);
            AssertEqual("pending birth rows respect configured cap", 2, cappedBirths.Count);
            AssertTrue("pending birth cap keeps newest ownership",
                cappedBirths.Any(row => row.snapshot.childId == "MiddleBirth")
                && cappedBirths.Any(row => row.snapshot.childId == "NewBirth")
                && !cappedBirths.Any(row => row.snapshot.childId == "OldBirth"));

            List<PendingBiotechBirthState> exactBirthCap = PendingBiotechBirthPolicy.Normalize(
                new List<PendingBiotechBirthState>
                {
                    PendingBirth("ExactBirthA", 100),
                    PendingBirth("ExactBirthB", 200)
                },
                currentTick: 500,
                maximumRows: 2);
            AssertEqual("pending birth exact-cap boundary keeps every row", 2, exactBirthCap.Count);

            List<PendingBiotechBirthState> manyBirthRows = new List<PendingBiotechBirthState>();
            for (int i = 0; i < 300; i++)
            {
                manyBirthRows.Add(PendingBirth("BirthCap_" + i, i + 1));
            }

            AssertEqual("hard ceiling preserves pending births above the XML default", 300,
                PendingBiotechBirthPolicy.Normalize(
                    manyBirthRows,
                    currentTick: 500,
                    maximumRows: BiotechPendingOwnershipLimits.HardMaximumRows).Count);
            foreach (int invalidCap in new[] { 0, -1, 9999 })
            {
                List<PendingBiotechBirthState> repairedCap = PendingBiotechBirthPolicy.Normalize(
                    manyBirthRows,
                    currentTick: 500,
                    maximumRows: invalidCap);
                AssertEqual("invalid pending birth cap uses shipped default " + invalidCap,
                    BiotechPendingOwnershipLimits.DefaultMaximumRows,
                    repairedCap.Count);
                AssertTrue("invalid pending birth cap keeps newest rows " + invalidCap,
                    repairedCap.Any(row => row.snapshot.childId == "BirthCap_299")
                    && !repairedCap.Any(row => row.snapshot.childId == "BirthCap_0"));
            }

            AssertTrue("birth admission accepts the final free slot",
                BiotechPendingOwnershipLimits.CanAdmit(existingRows: 1, requestedMaximumRows: 2));
            AssertTrue("birth admission rejects a newcomer instead of evicting claimed ownership",
                !BiotechPendingOwnershipLimits.CanAdmit(existingRows: 2, requestedMaximumRows: 2));

            AssertTrue("durable birth row matches exact arc and child",
                BirthRecordPolicy.Matches(
                    BiotechEventDefNames.FamilyBirth,
                    birth.familyArcId,
                    birth.childId,
                    birth.familyArcId,
                    birth.childId));
            AssertTrue("different child cannot satisfy birth backstop",
                !BirthRecordPolicy.Matches(
                    BiotechEventDefNames.FamilyBirth,
                    birth.familyArcId,
                    "Different",
                    birth.familyArcId,
                    birth.childId));
        }

        private static void TestSettingsInheritance()
        {
            AssertTrue("growth absent uses new default", BiotechSettingsInheritance.GrowthEnabled(null, null, true));
            AssertTrue("growth legacy false inherited",
                !BiotechSettingsInheritance.GrowthEnabled(null, false, true));
            AssertTrue("growth legacy true inherited",
                BiotechSettingsInheritance.GrowthEnabled(null, true, false));
            AssertTrue("growth new override wins",
                !BiotechSettingsInheritance.GrowthEnabled(false, true, true));

            AssertTrue("nonritual birth absent uses default",
                BiotechSettingsInheritance.FamilyBirthEnabled(null, null, null, false, true));
            AssertTrue("nonritual talelife false inherited",
                !BiotechSettingsInheritance.FamilyBirthEnabled(null, false, null, false, true));
            AssertTrue("nonritual talelife true inherited",
                BiotechSettingsInheritance.FamilyBirthEnabled(null, true, null, false, false));
            AssertTrue("ritual either explicit true enables",
                BiotechSettingsInheritance.FamilyBirthEnabled(null, false, true, true, false));
            AssertTrue("ritual explicit false with no true disables",
                !BiotechSettingsInheritance.FamilyBirthEnabled(null, false, null, true, true));
            AssertTrue("birth new override wins",
                !BiotechSettingsInheritance.FamilyBirthEnabled(false, true, true, true, true));
        }

        private static void TestContextFormatting()
        {
            GrowthMomentMutation mutation = new GrowthMomentMutation
            {
                childId = "Child;1",
                age = 10,
                stageToken = "age_10",
                familyArcId = "biotech-family|Child_1",
                opportunityBand = "mixed",
                selectedTrait = Trait("Tough", "tough", "line one;\nline two"),
                nicknameChanged = true,
                previousShortName = "Old",
                currentShortName = "New",
                newResponsibilities = true,
                supporter = new FamilySupportSelection
                {
                    adultId = "Adult_1",
                    displayName = "Morgan",
                    roleToken = BiotechFamilyRoleTokens.Teacher,
                    observationBand = "steady"
                }
            };
            for (int i = 0; i < 5; i++)
            {
                mutation.passionChanges.Add(new PassionMutation
                {
                    skillDefName = "Skill" + i,
                    label = "skill " + i,
                    beforePassion = i == 0 ? "none" : "minor",
                    afterPassion = i == 0 ? "minor" : "major"
                });
            }

            string context = GrowthMomentContextFormatter.Build(
                mutation, "a mixed range", "steady observed teaching",
                BiotechFamilyRoleTokens.Child, BiotechFamilyRoleTokens.Teacher,
                "localized new interest", "localized deepened interest");
            AssertContains("growth marker", context, "growth_moment=true");
            AssertContains("growth sanitized child id", context, "child_id=Child,1");
            AssertContains("growth opportunity prose", context, "opportunity_description=a mixed range");
            AssertContains("growth trait", context, "selected_trait=tough");
            AssertContains("growth sanitized trait description", context, "selected_trait_description=line one, line two");
            AssertContains("new interest wording", context, "interest_change_1=localized new interest");
            AssertContains("deepened interest wording", context, "interest_change_2=localized deepened interest");
            AssertTrue("hardcoded English interest prose absent",
                !context.Contains("interest_change_1=new interest")
                && !context.Contains("interest_change_2=deepened interest"));
            AssertContains("fourth interest retained", context, "new_interest_4=skill 3");
            AssertTrue("fifth interest capped", !context.Contains("new_interest_5="));
            AssertTrue("raw growth tier absent", !context.Contains("growthTier") && !context.Contains("tier=6"));
            AssertTrue("raw passion enum absent", !context.Contains("Minor") && !context.Contains("Major"));
            AssertTrue("raw observation counts absent", !context.Contains("lessonCount") && !context.Contains("evidenceCount"));
            AssertTrue("work list absent", !context.Contains("work_type") && !context.Contains("workTypes"));

            BirthMutationSnapshot birth = new BirthMutationSnapshot
            {
                familyArcId = "biotech-family|Child_1",
                childId = "Child_1",
                currentChildName = "Robin",
                outcomeToken = BiotechBirthOutcomeTokens.Healthy,
                methodToken = BiotechBirthMethodTokens.Surrogacy,
                birther = Participant("Birther", "Ari", true),
                geneticMother = Participant("Mother", "Bo", true),
                father = Participant("Father", "Cy", true),
                doctor = Participant("Doctor", "Dee", true),
                birtherDied = true,
                ritualBirth = true,
                namingDeadline = 999,
                birthTick = 123
            };
            BirthWriterSelection writers = BirthOwnershipPolicy.SelectWriters(birth,
                BiotechPolicySnapshot.CreateDefault());
            string birthContext = BirthContextFormatter.Build(birth, writers);
            AssertContains("birth tale-domain route", birthContext, "tale=BiotechFamilyBirth");
            AssertContains("birth marker", birthContext, "biotech_birth=true");
            AssertContains("birth exact outcome", birthContext, "birth_outcome=healthy");
            AssertContains("birth exact method", birthContext, "birth_method=surrogacy");
            AssertContains("birth distinct mother", birthContext, "genetic_mother_id=Mother");
            AssertContains("birth writer roles", birthContext, "initiator_family_role=birther");
            AssertContains("birth second writer role", birthContext, "recipient_family_role=genetic_mother");
            AssertTrue("birth internal tick/deadline absent",
                !birthContext.Contains("birthTick") && !birthContext.Contains("namingDeadline")
                && !birthContext.Contains("999") && !birthContext.Contains("123"));
        }

        private static void TestMechanitorLifecyclePolicy()
        {
            MechanitorMechSnapshot numerical = new MechanitorMechSnapshot
            {
                mechId = "Mech_1",
                displayName = "Lifter 1",
                hasExplicitName = true,
                numericalName = true,
                relationStartTick = 100,
                controlled = true
            };
            MechanitorMechSnapshot custom = new MechanitorMechSnapshot
            {
                mechId = "Mech_2",
                displayName = "Moss",
                hasExplicitName = true,
                numericalName = false,
                relationStartTick = 100
            };
            AssertTrue("numerical vanilla mech name is not player identity",
                !MechanitorLifecyclePolicy.IsPlayerNamed(numerical));
            AssertTrue("custom mech name is player identity",
                MechanitorLifecyclePolicy.IsPlayerNamed(custom));
            AssertTrue("tenure just below boundary is routine",
                !MechanitorLifecyclePolicy.IsLongServing(100, 999, 900));
            AssertTrue("tenure boundary is inclusive",
                MechanitorLifecyclePolicy.IsLongServing(100, 1000, 900));
            AssertTrue("custom name records immediate loss",
                MechanitorLifecyclePolicy.ShouldRecordLoss(custom, 100, 101, 900));
            AssertTrue("recent numerical mech loss stays silent",
                !MechanitorLifecyclePolicy.ShouldRecordLoss(numerical, 100, 101, 900));

            List<string> first = new List<string> { "KilledMelee" };
            List<string> second = new List<string> { "KilledBy" };
            AssertEqual("first-pawn combat role", 1,
                MechanitorLifecyclePolicy.CombatInstigatorRole("killedmelee", first, second));
            AssertEqual("second-pawn combat role", 2,
                MechanitorLifecyclePolicy.CombatInstigatorRole("KilledBy", first, second));
            AssertEqual("unconfigured Tale is not owned", 0,
                MechanitorLifecyclePolicy.CombatInstigatorRole("Downed", first, second));

            MechanitorObservationState oldSave = new MechanitorObservationState();
            MechanitorControllerSnapshot baseline = new MechanitorControllerSnapshot
            {
                hasMechlink = true,
                overseenMechs = new List<MechanitorMechSnapshot> { numerical }
            };
            oldSave.Baseline(baseline, 500, maximumMechs: 64);
            AssertTrue("old-save baseline initializes", oldSave.IsInitialized());
            AssertTrue("old-save existing control consumes first page", oldSave.firstControlledPageConsumed);
            AssertTrue("old-save existing control consumes first combat", oldSave.firstControlledCombatPageConsumed);
            AssertEqual("old-save tenure begins at first Pawn Diary observation", 500,
                oldSave.observedMechs[0].firstObservedTick);

            numerical.controlled = false;
            MechanitorObservationState overseenButDisconnected = new MechanitorObservationState();
            overseenButDisconnected.Baseline(baseline, 500, 64);
            AssertTrue("overseen but uncontrolled baseline leaves first combat available",
                !overseenButDisconnected.firstControlledCombatPageConsumed);
            numerical.controlled = true;

            MechanitorObservationState newController = new MechanitorObservationState();
            newController.Baseline(new MechanitorControllerSnapshot { hasMechlink = true }, 500, 64);
            AssertTrue("empty baseline leaves first page available", !newController.firstControlledPageConsumed);
            AssertTrue("empty baseline leaves first combat available",
                !newController.firstControlledCombatPageConsumed);
            newController.ObserveMech(custom, 500, maximumMechs: 1);
            newController.ObserveMech(numerical, 500, maximumMechs: 1);
            AssertEqual("XML mech cap does not evict established ownership", 1, newController.observedMechs.Count);
            AssertEqual("first admitted mech retained", "Mech_2", newController.observedMechs[0].mechId);

            newController.observedMechs[0].lossObserved = true;
            MechanitorMechObservationState recycled = newController.ObserveMech(
                numerical, 700, maximumMechs: 1);
            AssertNotNull("completed mech row is recycled at the admission cap", recycled);
            AssertEqual("new active ownership replaces completed history", "Mech_1",
                newController.observedMechs[0].mechId);

            MechanitorObservationState allActive = new MechanitorObservationState();
            allActive.Baseline(new MechanitorControllerSnapshot(), 500, maximumMechs: 1);
            allActive.ObserveMech(custom, 500, maximumMechs: 1);
            AssertTrue("active ownership is never evicted to admit another mech",
                allActive.ObserveMech(numerical, 700, maximumMechs: 1) == null);

            MechanitorBossCallObservationState boss = new MechanitorBossCallObservationState
            {
                bossgroupDefName = "Bossgroup_Diabolus",
                bossDefName = "Boss_Diabolus",
                bossKindDefName = "Diabolus",
                bossLabel = "Diabolus",
                calledTick = 600
            };
            newController.bossCalls.Add(boss);
            newController.Normalize(1, 1);
            AssertTrue("boss call begins unresolved", !newController.bossCalls[0].defeatedObserved);
            newController.bossCalls[0].defeatedObserved = true;
            AssertTrue("defeat semantics preserve prior call", newController.bossCalls[0].defeatedObserved
                && newController.bossCalls[0].calledTick == 600);

            MechanitorBossCallObservationState olderCall = BossCall("Diabolus", 800);
            MechanitorBossCallObservationState newerCall = BossCall("Diabolus", 900);
            List<MechanitorBossOwnershipCandidate> candidates = new List<MechanitorBossOwnershipCandidate>
            {
                new MechanitorBossOwnershipCandidate { ownerId = "Controller_B", call = newerCall },
                new MechanitorBossOwnershipCandidate { ownerId = "Controller_A", call = olderCall }
            };
            MechanitorBossOwnershipCandidate firstSpawn = MechanitorLifecyclePolicy.AssignSpawnedBoss(
                candidates, "Diabolus", "Pawn_Boss_1");
            MechanitorBossOwnershipCandidate secondSpawn = MechanitorLifecyclePolicy.AssignSpawnedBoss(
                candidates, "Diabolus", "Pawn_Boss_2");
            AssertEqual("first spawned boss owns oldest global call", "Controller_A", firstSpawn.ownerId);
            AssertEqual("second spawned boss owns remaining call", "Controller_B", secondSpawn.ownerId);
            AssertEqual("spawn assignment saves exact pawn ID", "Pawn_Boss_1", olderCall.bossPawnId);
            AssertEqual("exact death resolves only its assigned controller", "Controller_B",
                MechanitorLifecyclePolicy.FindDefeatedBoss(
                    candidates, "Diabolus", "Pawn_Boss_2").ownerId);

            List<MechanitorBossOwnershipCandidate> ambiguousLegacy =
                new List<MechanitorBossOwnershipCandidate>
                {
                    new MechanitorBossOwnershipCandidate
                    {
                        ownerId = "Controller_A",
                        call = BossCall("WarQueen", 1000)
                    },
                    new MechanitorBossOwnershipCandidate
                    {
                        ownerId = "Controller_B",
                        call = BossCall("WarQueen", 1100)
                    }
                };
            AssertTrue("ambiguous legacy same-kind death fails closed",
                MechanitorLifecyclePolicy.FindDefeatedBoss(
                    ambiguousLegacy, "WarQueen", "Pawn_Boss_3") == null);
            ambiguousLegacy.RemoveAt(1);
            AssertEqual("unique legacy call receives safe exact-ID backfill", "Controller_A",
                MechanitorLifecyclePolicy.FindDefeatedBoss(
                    ambiguousLegacy, "WarQueen", "Pawn_Boss_3").ownerId);
        }

        private static MechanitorBossCallObservationState BossCall(string kindDefName, int calledTick)
        {
            return new MechanitorBossCallObservationState
            {
                bossgroupDefName = "Bossgroup_" + kindDefName,
                bossDefName = "Boss_" + kindDefName,
                bossKindDefName = kindDefName,
                bossLabel = kindDefName,
                calledTick = calledTick
            };
        }

        private static void TestPsychicBondLifecyclePolicy()
        {
            PsychicBondPair sorted = PsychicBondPairPolicy.Create("Pawn_Z", " Pawn_A ");
            AssertNotNull("distinct bond IDs form a pair", sorted);
            AssertEqual("bond pair sorts first ID", "Pawn_A", sorted.firstPawnId);
            AssertEqual("bond pair sorts second ID", "Pawn_Z", sorted.secondPawnId);
            AssertEqual("bond pair key is stable", "Pawn_A|Pawn_Z", sorted.Key);
            AssertTrue("blank bond ID is rejected",
                PsychicBondPairPolicy.Create(string.Empty, "Pawn_A") == null);
            AssertTrue("self bond is rejected",
                PsychicBondPairPolicy.Create("Pawn_A", "Pawn_A") == null);
            AssertTrue("separator-bearing IDs are rejected",
                PsychicBondPairPolicy.Create("Pawn|A", "Pawn_B") == null);
            AssertEqual("bond arc includes sorted pair and epoch",
                "biotech-psychic-bond|Pawn_A|Pawn_Z|3",
                PsychicBondPairPolicy.ArcKey(sorted, 3));
            AssertTrue("same sorted pair and phase is recursive secondary",
                PsychicBondLifecyclePolicy.IsRecursiveSecondary(
                    PsychicBondPairPolicy.Create("Pawn_Z", "Pawn_A"),
                    PsychicBondPhaseTokens.Formed,
                    sorted.Key,
                    PsychicBondPhaseTokens.Formed));
            AssertTrue("different phase cannot steal recursive ownership",
                !PsychicBondLifecyclePolicy.IsRecursiveSecondary(
                    sorted,
                    PsychicBondPhaseTokens.Ruptured,
                    sorted.Key,
                    PsychicBondPhaseTokens.Formed));
            AssertTrue("different pair cannot steal recursive ownership",
                !PsychicBondLifecyclePolicy.IsRecursiveSecondary(
                    PsychicBondPairPolicy.Create("Pawn_A", "Pawn_B"),
                    PsychicBondPhaseTokens.Formed,
                    sorted.Key,
                    PsychicBondPhaseTokens.Formed));
            AssertTrue("formation owns only nested PsychicBond",
                PsychicBondLifecyclePolicy.OwnsNestedSignalDef(
                    PsychicBondPhaseTokens.Formed,
                    "PsychicBond")
                && !PsychicBondLifecyclePolicy.OwnsNestedSignalDef(
                    PsychicBondPhaseTokens.Formed,
                    "PsychicBondTorn"));
            AssertTrue("rupture owns only nested PsychicBondTorn",
                PsychicBondLifecyclePolicy.OwnsNestedSignalDef(
                    PsychicBondPhaseTokens.Ruptured,
                    "PsychicBondTorn")
                && !PsychicBondLifecyclePolicy.OwnsNestedSignalDef(
                    PsychicBondPhaseTokens.Ruptured,
                    "PsychicBond"));

            PsychicBondMutationSnapshot formed = new PsychicBondMutationSnapshot
            {
                firstPawnId = "Pawn_Z",
                secondPawnId = "Pawn_A",
                phase = PsychicBondPhaseTokens.Formed,
                bondEpoch = 1,
                mutuallyBondedBefore = false,
                mutuallyBondedAfter = true
            };
            AssertTrue("verified new mutual bond is owned",
                PsychicBondLifecyclePolicy.ShouldOwnFormation(formed));
            formed.mutuallyBondedBefore = true;
            AssertTrue("prior mutual bond suppresses formation replay",
                !PsychicBondLifecyclePolicy.ShouldOwnFormation(formed));
            formed.mutuallyBondedBefore = false;
            formed.mutuallyBondedAfter = false;
            AssertTrue("failed mutual verification suppresses formation",
                !PsychicBondLifecyclePolicy.ShouldOwnFormation(formed));

            PsychicBondMutationSnapshot ruptured = new PsychicBondMutationSnapshot
            {
                firstPawnId = "Pawn_A",
                secondPawnId = "Pawn_Z",
                phase = PsychicBondPhaseTokens.Ruptured,
                bondEpoch = 4,
                mutuallyBondedBefore = true,
                mutuallyBondedAfter = false
            };
            AssertTrue("verified mutual rupture is owned",
                PsychicBondLifecyclePolicy.ShouldOwnRupture(ruptured));
            ruptured.mutuallyBondedAfter = true;
            AssertTrue("still-mutual pair suppresses rupture",
                !PsychicBondLifecyclePolicy.ShouldOwnRupture(ruptured));

            AssertEqual("exact death outranks gene-removal scope",
                PsychicBondCauseTokens.Death,
                PsychicBondLifecyclePolicy.ExactRuptureCause(true, true));
            AssertEqual("owning gene-removal scope is truthful",
                PsychicBondCauseTokens.GeneRemoved,
                PsychicBondLifecyclePolicy.ExactRuptureCause(false, true));
            AssertEqual("unproved rupture cause stays unknown",
                PsychicBondCauseTokens.Unknown,
                PsychicBondLifecyclePolicy.ExactRuptureCause(false, false));
            AssertTrue("unknown cause is not prompt-safe",
                !PsychicBondCauseTokens.IsPromptSafe(PsychicBondCauseTokens.Unknown));

            List<PsychicBondObservationRow> firstRows = new List<PsychicBondObservationRow>
            {
                new PsychicBondObservationRow
                {
                    partnerPawnId = "Pawn_Z",
                    bondEpoch = 2,
                    lastTransitionTick = 50
                }
            };
            List<PsychicBondObservationRow> secondRows = new List<PsychicBondObservationRow>
            {
                new PsychicBondObservationRow
                {
                    partnerPawnId = "Pawn_A",
                    bondEpoch = 3,
                    lastTransitionTick = 60
                }
            };
            AssertEqual("reformation increments the greatest persisted pair epoch", 4,
                PsychicBondLifecyclePolicy.NextEpoch("Pawn_A", "Pawn_Z", firstRows, secondRows));
            AssertEqual("new pair starts at epoch one", 1,
                PsychicBondLifecyclePolicy.NextEpoch("Pawn_A", "Pawn_B", firstRows, null));

            List<PsychicBondObservationRow> malformed = new List<PsychicBondObservationRow>
            {
                null,
                new PsychicBondObservationRow { partnerPawnId = "", bondEpoch = -2, lastTransitionTick = -4 },
                new PsychicBondObservationRow
                    { partnerPawnId = "Pawn_A", bondEpoch = 1, lastTransitionTick = 10 },
                new PsychicBondObservationRow
                    { partnerPawnId = "Pawn_Z", bondEpoch = 1, lastTransitionTick = 20 },
                new PsychicBondObservationRow
                    { partnerPawnId = " Pawn_Z ", bondEpoch = 2, lastTransitionTick = 30 },
                new PsychicBondObservationRow
                    { partnerPawnId = "Pawn_B", bondEpoch = 1, lastTransitionTick = 999 }
            };
            PsychicBondLifecyclePolicy.NormalizeRows(malformed, "Pawn_A", 100, 2);
            AssertEqual("normalization rejects invalid/self rows and applies cap", 2, malformed.Count);
            AssertSequence("normalization is deterministic by partner ID",
                new[] { "Pawn_B", "Pawn_Z" },
                malformed.Select(row => row.partnerPawnId));
            AssertEqual("newest duplicate bond row survives", 2,
                malformed.Single(row => row.partnerPawnId == "Pawn_Z").bondEpoch);
            AssertEqual("future transition tick clamps to now", 100,
                malformed.Single(row => row.partnerPawnId == "Pawn_B").lastTransitionTick);
        }

        private static void TestDeathrestInterruptionPolicy()
        {
            BiotechBondDeathrestPolicySnapshot policy =
                BiotechBondDeathrestPolicySnapshot.CreateDefault();
            policy.deathrestLifetimePageLimit = 2;
            policy.deathrestCooldownTicks = 100;
            DeathrestObservationState state = new DeathrestObservationState();
            DeathrestMutationSnapshot snapshot = new DeathrestMutationSnapshot
            {
                pawnId = "Pawn_A",
                pawnEligible = true,
                activeDeathrestBefore = true,
                interruptedHediffAfter = true,
                deathrestPercentBefore = 0.5f,
                observedTick = 1000
            };
            AssertEqual("severe threshold boundary records",
                DeathrestInterruptionDecision.OwnAndRecord,
                DeathrestInterruptionPolicy.Decide(snapshot, state, policy));
            snapshot.deathrestPercentBefore = 0.5001f;
            AssertEqual("above severe threshold owns silently",
                DeathrestInterruptionDecision.OwnSilently,
                DeathrestInterruptionPolicy.Decide(snapshot, state, policy));
            snapshot.deathrestPercentBefore = 0.2f;
            DeathrestInterruptionPolicy.Record(state, 1000);
            snapshot.observedTick = 1099;
            AssertEqual("cooldown just before boundary is silent",
                DeathrestInterruptionDecision.OwnSilently,
                DeathrestInterruptionPolicy.Decide(snapshot, state, policy));
            snapshot.observedTick = 1100;
            AssertEqual("cooldown boundary is inclusive",
                DeathrestInterruptionDecision.OwnAndRecord,
                DeathrestInterruptionPolicy.Decide(snapshot, state, policy));
            DeathrestInterruptionPolicy.Record(state, 1100);
            snapshot.observedTick = 2000;
            AssertEqual("lifetime cap keeps later severe wake silent",
                DeathrestInterruptionDecision.OwnSilently,
                DeathrestInterruptionPolicy.Decide(snapshot, state, policy));

            DeathrestMutationSnapshot inactive = new DeathrestMutationSnapshot
            {
                pawnId = "Pawn_A",
                pawnEligible = true,
                activeDeathrestBefore = false,
                interruptedHediffAfter = true,
                deathrestPercentBefore = 0.1f
            };
            AssertEqual("inactive deathrest is dropped",
                DeathrestInterruptionDecision.Drop,
                DeathrestInterruptionPolicy.Decide(inactive, new DeathrestObservationState(), policy));
            inactive.activeDeathrestBefore = true;
            inactive.interruptedHediffAfter = false;
            AssertEqual("unconfirmed interrupted hediff is dropped",
                DeathrestInterruptionDecision.Drop,
                DeathrestInterruptionPolicy.Decide(inactive, new DeathrestObservationState(), policy));
            inactive.interruptedHediffAfter = true;
            inactive.deathrestPercentBefore = 1f;
            AssertEqual("completed wake is silent and unowned",
                DeathrestInterruptionDecision.Drop,
                DeathrestInterruptionPolicy.Decide(inactive, new DeathrestObservationState(), policy));
            inactive.deathrestPercentBefore = float.NaN;
            AssertEqual("invalid completion percent is dropped",
                DeathrestInterruptionDecision.Drop,
                DeathrestInterruptionPolicy.Decide(inactive, new DeathrestObservationState(), policy));

            DeathrestObservationState malformed = new DeathrestObservationState
            {
                observationVersion = -1,
                severeInterruptionsRecorded = -3,
                lastRecordedTick = 999
            };
            DeathrestInterruptionPolicy.Normalize(malformed, 100);
            AssertEqual("deathrest version clamps", 0, malformed.observationVersion);
            AssertEqual("deathrest count clamps", 0, malformed.severeInterruptionsRecorded);
            AssertEqual("deathrest future tick clamps", 100, malformed.lastRecordedTick);
        }

        private static void TestShippedXmlPolicyAndLocalization()
        {
            string root = RepositoryRoot();
            XDocument policy = XDocument.Load(Path.Combine(root, "1.6", "Defs", "DiaryBiotechPolicyDefs.xml"));
            XElement def = policy.Descendants("PawnDiary.DiaryBiotechPolicyDef").Single();
            AssertEqual("policy defName", "Diary_BiotechPolicy", Value(def, "defName"));
            AssertEqual("policy max birth writers", "2", Value(def, "maximumBirthWriters"));
            AssertEqual("policy max pending growth rows", "256", Value(def, "maximumPendingGrowthRows"));
            AssertEqual("policy max pending birth rows", "256", Value(def, "maximumPendingBirthRows"));
            AssertEqual("policy birth correlation expiry", "2500", Value(def, "birthCorrelationExpiryTicks"));
            AssertEqual("policy supporter minimum", "2", Value(def, "supporterMinimumEvidence"));
            AssertEqual("gene theme cap", "4", Value(def, "geneMaximumThemes"));
            AssertEqual("gene delta bonus", "100", Value(def, "geneDeltaBonus"));
            AssertEqual("gene duplicate-category penalty", "30", Value(def, "geneDuplicateCategoryPenalty"));
            AssertEqual("gene label cap", "80", Value(def, "geneLabelCharacterLimit"));
            AssertEqual("gene description cap", "240", Value(def, "geneDescriptionCharacterLimit"));
            AssertEqual("gene total text cap", "640", Value(def, "geneTotalTextCharacterLimit"));
            AssertEqual("gene observed membership cap", "512", Value(def, "geneMaximumObservedDefNames"));
            AssertEqual("gene fallback significance", "2", Value(def, "geneMinimumFallbackChanges"));
            AssertEqual("mechanitor long-service threshold", "900000",
                Value(def, "mechanitorLongServiceTicks"));
            AssertEqual("mechanitor observed-mech cap", "64",
                Value(def, "mechanitorMaximumObservedMechs"));
            AssertEqual("mechanitor boss-call cap", "16",
                Value(def, "mechanitorMaximumBossCalls"));
            AssertSequence("mechanitor first-pawn combat Tales", new[]
            {
                "KilledLongRange", "KilledMelee", "KilledMajorThreat", "DefeatedHostileFactionLeader"
            }, Values(def.Element("mechanitorCombatFirstPawnDefNames")));
            AssertSequence("mechanitor second-pawn combat Tales", new[] { "KilledBy" },
                Values(def.Element("mechanitorCombatSecondPawnDefNames")));
            List<XElement> geneWeights = def.Element("geneCategoryWeights").Elements("li").ToList();
            AssertSequence("gene structural categories", new[]
            {
                "ability", "trait", "resource", "need", "aging", "environment", "violence",
                "emotion", "social", "capacity", "appearance", "stat", "other"
            }, geneWeights.Select(row => Value(row, "category")));
            AssertEqual("gene categories are unique", geneWeights.Count,
                geneWeights.Select(row => Value(row, "category")).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            AssertTrue("gene correction lists contain no unconditional DLC Def reference",
                Values(def.Element("geneForceIncludeDefNames")).Length == 0
                && Values(def.Element("geneExcludeDefNames")).Length == 0);
            AssertTrue("new-interest prompt prose is XML-owned",
                Value(def, "newInterestDescription").Length > 0);
            AssertTrue("deepened-interest prompt prose is XML-owned",
                Value(def, "deepenedInterestDescription").Length > 0);
            AssertTrue("N3-B gene-identity narrative prose is XML-owned",
                Value(def, "geneIdentityNarrativeFormat").Length > 0);
            AssertTrue("policy exact BabyPlay matcher",
                Values(def.Element("familyActivityExactDefNames")).Contains("BabyPlay"));
            AssertTrue("policy Lesson prefix",
                Values(def.Element("familyActivityPrefixes")).Contains("Lesson"));
            AssertSequence("policy accepted adult lesson memory", new[] { "GaveLesson" },
                Values(def.Element("familyLessonAdultThoughtDefNames")));
            AssertSequence("policy accepted child lesson memory", new[] { "WasTaught" },
                Values(def.Element("familyLessonChildThoughtDefNames")));
            AssertSequence("policy pregnancy Hediffs", new[] { "PregnantHuman", "Pregnant" },
                Values(def.Element("familyPregnancyHediffDefNames")));
            AssertSequence("policy labor Hediffs", new[] { "PregnancyLabor", "PregnancyLaborPushing" },
                Values(def.Element("familyLaborHediffDefNames")));
            AssertSequence("policy mature birth correlation Defs",
                new[] { "GaveBirth", "BabyBorn", "Stillbirth" },
                Values(def.Element("matureBirthDefNames")));
            AssertSequence("policy birther miscarriage Thought Defs", new[] { "Miscarried" },
                Values(def.Element("miscarriageBirtherThoughtDefNames")));
            AssertSequence("policy partner miscarriage Thought Defs", new[] { "PartnerMiscarried" },
                Values(def.Element("miscarriagePartnerThoughtDefNames")));
            AssertEqual("deathrest severe threshold", "0.5",
                Value(def, "deathrestSevereCompletionThreshold"));
            AssertEqual("deathrest cooldown", "900000", Value(def, "deathrestCooldownTicks"));
            AssertEqual("deathrest lifetime cap", "1", Value(def, "deathrestLifetimePageLimit"));
            AssertEqual("psychic-bond correlation expiry", "2500",
                Value(def, "psychicBondCorrelationExpiryTicks"));
            AssertEqual("psychic-bond saved-row cap", "16",
                Value(def, "maximumBondObservationRows"));
            AssertTrue("psychic-bond narrative prose is XML-owned",
                Value(def, "psychicBondNarrativeFormat").Length > 0);

            List<XElement> opportunity = def.Element("opportunityBands").Elements("li").ToList();
            AssertEqual("four opportunity bands", 4, opportunity.Count);
            bool[] covered = new bool[9];
            foreach (XElement band in opportunity)
            {
                int minimum = int.Parse(Value(band, "minimumTier"));
                int maximum = int.Parse(Value(band, "maximumTier"));
                AssertTrue("opportunity token nonblank", Value(band, "token").Length > 0);
                AssertTrue("opportunity description nonblank", Value(band, "description").Length > 0);
                for (int tier = minimum; tier <= maximum; tier++)
                {
                    AssertTrue("opportunity tier unique " + tier, !covered[tier]);
                    covered[tier] = true;
                }
            }
            AssertTrue("opportunity covers every tier", covered.All(value => value));

            XDocument groups = XDocument.Load(Path.Combine(root, "1.6", "Defs", "DiaryInteractionGroupDefs.xml"));
            XElement growth = Group(groups, "progressionGrowthMoment");
            XElement birth = Group(groups, "biotechFamilyBirth");
            XElement geneIdentity = Group(groups, "progressionXenotype");
            XElement mechanitor = Group(groups, "progressionMechanitorLifecycle");
            XElement psychicBond = Group(groups, "biotechPsychicBondLifecycle");
            XElement deathrest = Group(groups, "biotechDeathrestInterrupted");
            AssertEqual("growth group order", "800", Value(growth, "order"));
            AssertEqual("growth domain", "Progression", Value(growth, "domain"));
            AssertSequence("growth exact classifier", new[] { "BiotechGrowthMoment" },
                Values(growth.Element("matchDefNames")));
            AssertEqual("birth group order", "315", Value(birth, "order"));
            AssertEqual("birth domain", "Tale", Value(birth, "domain"));
            AssertSequence("birth exact classifier", new[] { "BiotechFamilyBirth" },
                Values(birth.Element("matchDefNames")));
            AssertSequence("gene identity classifier retains legacy and rich tokens",
                new[] { "XenotypeChanged", "GeneIdentityChanged" },
                Values(geneIdentity.Element("matchDefNames")));
            AssertSequence("growth package gate", new[] { "Ludeon.RimWorld.Biotech" },
                Values(growth.Element("enableWhenPackageIdsLoaded")));
            AssertSequence("birth package gate", new[] { "Ludeon.RimWorld.Biotech" },
                Values(birth.Element("enableWhenPackageIdsLoaded")));
            AssertSequence("gene identity package gate", new[] { "Ludeon.RimWorld.Biotech" },
                Values(geneIdentity.Element("enableWhenPackageIdsLoaded")));
            AssertEqual("mechanitor group order", "798", Value(mechanitor, "order"));
            AssertEqual("mechanitor domain", "Progression", Value(mechanitor, "domain"));
            AssertSequence("mechanitor exact classifiers", new[]
            {
                MechanitorEventDefNames.MechlinkInstalled,
                MechanitorEventDefNames.MechlinkRemoved,
                MechanitorEventDefNames.FirstControlledMech,
                MechanitorEventDefNames.FirstControlledMechCombat,
                MechanitorEventDefNames.SignificantMechLoss,
                MechanitorEventDefNames.BossCalled,
                MechanitorEventDefNames.BossDefeated
            }, Values(mechanitor.Element("matchDefNames")));
            AssertSequence("mechanitor package gate", new[] { "Ludeon.RimWorld.Biotech" },
                Values(mechanitor.Element("enableWhenPackageIdsLoaded")));
            AssertEqual("psychic-bond group order", "796", Value(psychicBond, "order"));
            AssertSequence("psychic-bond exact classifiers", new[]
            {
                BiotechBondDeathrestEventDefNames.PsychicBondFormed,
                BiotechBondDeathrestEventDefNames.PsychicBondRuptured
            }, Values(psychicBond.Element("matchDefNames")));
            AssertSequence("psychic-bond package gate", new[] { "Ludeon.RimWorld.Biotech" },
                Values(psychicBond.Element("enableWhenPackageIdsLoaded")));
            AssertTrue("psychic-bond prompt forbids unsupported relationship claims",
                Value(psychicBond, "instruction").Contains("never infer romance, consent, destiny")
                    && Value(psychicBond, "instruction").Contains("permanence"));
            AssertEqual("deathrest group order", "797", Value(deathrest, "order"));
            AssertSequence("deathrest exact classifier",
                new[] { BiotechBondDeathrestEventDefNames.DeathrestInterrupted },
                Values(deathrest.Element("matchDefNames")));
            AssertSequence("deathrest package gate", new[] { "Ludeon.RimWorld.Biotech" },
                Values(deathrest.Element("enableWhenPackageIdsLoaded")));
            AssertTrue("deathrest prompt omits unproved causes and routine wakes",
                Value(deathrest, "instruction").Contains("without inventing")
                    && Value(deathrest, "instruction").Contains("routine completed wake"));
            AssertTrue("growth has no token/catch-all matcher",
                growth.Element("matchTokens") == null && Value(growth, "catchAll") != "true");
            AssertTrue("birth has no token/catch-all matcher",
                birth.Element("matchTokens") == null && Value(birth, "catchAll") != "true");

            AssertEqual("growth order slot is reserved once", 1,
                groups.Root.Elements("PawnDiary.DiaryInteractionGroupDef").Count(group =>
                    Value(group, "domain") == "Progression" && Value(group, "order") == "800"));
            AssertEqual("birth order slot is reserved once", 1,
                groups.Root.Elements("PawnDiary.DiaryInteractionGroupDef").Count(group =>
                    Value(group, "domain") == "Tale" && Value(group, "order") == "315"));

            // Biotech is optional content, never a mod dependency. The package-gated groups above
            // should simply stay disabled in a base-game install.
            XDocument about = XDocument.Load(Path.Combine(root, "About", "About.xml"));
            AssertTrue("About.xml does not require Biotech",
                !about.Descendants("modDependencies").Descendants("packageId").Any(packageId =>
                    string.Equals(packageId.Value.Trim(), "Ludeon.RimWorld.Biotech",
                        StringComparison.OrdinalIgnoreCase)));

            AssertLocalization(root, "English");
            AssertLocalization(root, "Russian (Русский)");
        }

        private static void AssertLocalization(string root, string language)
        {
            XDocument groups = XDocument.Load(Path.Combine(root, "Languages", language, "DefInjected",
                "PawnDiary.DiaryInteractionGroupDef", "DiaryInteractionGroupDefs.xml"));
            string[] groupKeys =
            {
                "progressionGrowthMoment.label", "progressionGrowthMoment.instruction",
                "progressionGrowthMoment.tone", "progressionGrowthMoment.tones.0",
                "progressionGrowthMoment.tones.1", "biotechFamilyBirth.label",
                "biotechFamilyBirth.instruction", "biotechFamilyBirth.tone",
                "biotechFamilyBirth.tones.0", "biotechFamilyBirth.tones.1",
                "progressionXenotype.label", "progressionXenotype.instruction"
                , "progressionMechanitorLifecycle.label",
                "progressionMechanitorLifecycle.instruction",
                "progressionMechanitorLifecycle.tone",
                "progressionMechanitorLifecycle.tones.0",
                "progressionMechanitorLifecycle.tones.1",
                "biotechPsychicBondLifecycle.label",
                "biotechPsychicBondLifecycle.instruction",
                "biotechPsychicBondLifecycle.tone",
                "biotechPsychicBondLifecycle.tones.0",
                "biotechPsychicBondLifecycle.tones.1",
                "biotechDeathrestInterrupted.label",
                "biotechDeathrestInterrupted.instruction",
                "biotechDeathrestInterrupted.tone",
                "biotechDeathrestInterrupted.tones.0",
                "biotechDeathrestInterrupted.tones.1"
            };
            foreach (string key in groupKeys)
            {
                AssertTrue(language + " group localization " + key,
                    groups.Root.Element(key) != null && groups.Root.Element(key).Value.Trim().Length > 0);
            }

            XDocument policy = XDocument.Load(Path.Combine(root, "Languages", language, "DefInjected",
                "PawnDiary.DiaryBiotechPolicyDef", "DiaryBiotechPolicyDefs.xml"));
            AssertTrue(language + " policy label", Value(policy.Root, "Diary_BiotechPolicy.label").Length > 0);
            AssertTrue(language + " new-interest description",
                Value(policy.Root, "Diary_BiotechPolicy.newInterestDescription").Length > 0);
            AssertTrue(language + " deepened-interest description",
                Value(policy.Root, "Diary_BiotechPolicy.deepenedInterestDescription").Length > 0);
            AssertTrue(language + " N3-B gene-identity narrative format",
                Value(policy.Root, "Diary_BiotechPolicy.geneIdentityNarrativeFormat").Length > 0);
            AssertTrue(language + " N3-B psychic-bond narrative format",
                Value(policy.Root, "Diary_BiotechPolicy.psychicBondNarrativeFormat").Length > 0);
            for (int i = 0; i < 4; i++)
            {
                AssertTrue(language + " opportunity description " + i,
                    Value(policy.Root, "Diary_BiotechPolicy.opportunityBands." + i + ".description").Length > 0);
            }
            for (int i = 0; i < 3; i++)
            {
                AssertTrue(language + " observation description " + i,
                    Value(policy.Root, "Diary_BiotechPolicy.observationBands." + i + ".description").Length > 0);
            }

            XDocument keyed = XDocument.Load(Path.Combine(root, "Languages", language, "Keyed", "PawnDiary.xml"));
            string[] keyedNames =
            {
                "PawnDiary.Event.Biotech.Growth.Fallback",
                "PawnDiary.Event.Biotech.Growth.Label",
                "PawnDiary.Event.Biotech.Growth.Summary.Trait",
                "PawnDiary.Event.Biotech.Growth.Summary.Identity",
                "PawnDiary.Event.Biotech.Growth.Summary.NewInterest",
                "PawnDiary.Event.Biotech.Growth.Summary.DeepenedInterest",
                "PawnDiary.Event.Biotech.Growth.Summary.Name",
                "PawnDiary.Event.Biotech.Growth.Summary.Responsibilities",
                "PawnDiary.Event.Biotech.Growth.Summary.Generic",
                "PawnDiary.Event.Biotech.Growth.SupporterFallback",
                "PawnDiary.Event.Biotech.Birth.Label",
                "PawnDiary.Event.Biotech.Birth.ChildFallback",
                "PawnDiary.Event.Biotech.Birth.Fallback",
                "PawnDiary.Event.Biotech.Birth.PairFallback",
                "PawnDiary.Event.Biotech.Birth.Outcome.Healthy",
                "PawnDiary.Event.Biotech.Birth.Outcome.InfantIllness",
                "PawnDiary.Event.Biotech.Birth.Outcome.Stillbirth",
                "PawnDiary.Event.Biotech.Birth.BirtherDied",
                "PawnDiary.Event.Biotech.GeneIdentity.Label",
                "PawnDiary.Event.Biotech.GeneIdentity.Text",
                "PawnDiary.Event.Biotech.Mechanitor.MechlinkInstalled.Label",
                "PawnDiary.Event.Biotech.Mechanitor.MechlinkInstalled.Text",
                "PawnDiary.Event.Biotech.Mechanitor.MechlinkRemoved.Label",
                "PawnDiary.Event.Biotech.Mechanitor.MechlinkRemoved.Text",
                "PawnDiary.Event.Biotech.Mechanitor.FirstMech.Label",
                "PawnDiary.Event.Biotech.Mechanitor.FirstMech.Text",
                "PawnDiary.Event.Biotech.Mechanitor.FirstCombat.Label",
                "PawnDiary.Event.Biotech.Mechanitor.FirstCombat.Text",
                "PawnDiary.Event.Biotech.Mechanitor.MechLoss.Label",
                "PawnDiary.Event.Biotech.Mechanitor.MechLoss.Text",
                "PawnDiary.Event.Biotech.Mechanitor.BossCalled.Label",
                "PawnDiary.Event.Biotech.Mechanitor.BossCalled.Text",
                "PawnDiary.Event.Biotech.Mechanitor.BossDefeated.Label",
                "PawnDiary.Event.Biotech.Mechanitor.BossDefeated.Text",
                "PawnDiary.Event.Biotech.Bond.Formed.Label",
                "PawnDiary.Event.Biotech.Bond.Formed.Text",
                "PawnDiary.Event.Biotech.Bond.Formed.Phase",
                "PawnDiary.Event.Biotech.Bond.Ruptured.Label",
                "PawnDiary.Event.Biotech.Bond.Ruptured.Text",
                "PawnDiary.Event.Biotech.Bond.Ruptured.Phase",
                "PawnDiary.Event.Biotech.Deathrest.Label",
                "PawnDiary.Event.Biotech.Deathrest.Text",
                "PawnDiary.Event.Biotech.Deathrest.Phase.Interrupted",
                "PawnDiary.Dev.PromptSuite.BiotechGrowth.Label",
                "PawnDiary.Dev.PromptSuite.BiotechGrowth.Markers",
                "PawnDiary.Dev.PromptSuite.BiotechGrowth.Initiator",
                "PawnDiary.Dev.PromptSuite.BiotechGrowth.Recipient",
                "PawnDiary.Dev.PromptSuite.BiotechBirth.Label",
                "PawnDiary.Dev.PromptSuite.BiotechBirth.Markers",
                "PawnDiary.Dev.PromptSuite.BiotechBirth.Initiator",
                "PawnDiary.Dev.PromptSuite.BiotechBirth.Recipient"
            };
            foreach (string key in keyedNames)
            {
                AssertTrue(language + " keyed fallback " + key, Value(keyed.Root, key).Length > 0);
            }

            string growthMarkers = Value(
                keyed.Root,
                "PawnDiary.Dev.PromptSuite.BiotechGrowth.Markers");
            AssertTrue(language + " growth preview uses a production opportunity token",
                growthMarkers.Contains("opportunity_band=broad"));
            AssertTrue(language + " growth preview uses production family-role tokens",
                growthMarkers.Contains("supporter_role=teacher")
                && growthMarkers.Contains("recipient_family_role=teacher")
                && !growthMarkers.Contains("observed_teacher"));
        }

        private static GrowthPawnSnapshot Snapshot(
            string id,
            int age,
            int tier,
            string name,
            bool responsibilities,
            IEnumerable<GrowthTraitFact> traits,
            IEnumerable<GrowthSkillFact> skills)
        {
            return new GrowthPawnSnapshot
            {
                pawnId = id,
                biologicalAge = age,
                growthTier = tier,
                shortName = name,
                hasNewResponsibilities = responsibilities,
                traits = traits == null ? new List<GrowthTraitFact>() : traits.ToList(),
                skills = skills == null ? new List<GrowthSkillFact>() : skills.ToList()
            };
        }

        private static PendingBiotechGrowthMoment Pending(
            string pawnId,
            int age,
            int birthdayTick,
            int configuredTick)
        {
            return new PendingBiotechGrowthMoment
            {
                pawnId = pawnId,
                birthdayAge = age,
                birthdayTick = birthdayTick,
                configuredTick = configuredTick,
                growthTier = 4,
                correlationId = BiotechArcKeys.GrowthCorrelation(pawnId, age),
                birthdaySnapshot = Snapshot(pawnId, age, 4, pawnId, false, null, null)
            };
        }

        private static GrowthTraitFact Trait(string key, string label, string description)
        {
            return new GrowthTraitFact { traitKey = key, label = label, description = description };
        }

        private static GeneFact Gene(string defName, string label, string category, string description = null)
        {
            GeneFact fact = new GeneFact
            {
                defName = defName,
                label = label,
                description = description ?? label + " description",
                isEndogene = true
            };
            switch (category)
            {
                case GeneCategoryTokens.Ability: fact.affectsAbility = true; break;
                case GeneCategoryTokens.Trait: fact.affectsTrait = true; break;
                case GeneCategoryTokens.Resource: fact.affectsResource = true; break;
                case GeneCategoryTokens.Need: fact.affectsNeed = true; break;
                case GeneCategoryTokens.Appearance: fact.affectsAppearance = true; break;
                case GeneCategoryTokens.Aging: fact.affectsAging = true; break;
                case GeneCategoryTokens.Environment: fact.affectsEnvironment = true; break;
                case GeneCategoryTokens.Violence: fact.affectsViolence = true; break;
                case GeneCategoryTokens.Emotion: fact.affectsEmotion = true; break;
                case GeneCategoryTokens.Social: fact.affectsSocial = true; break;
                case GeneCategoryTokens.Capacity: fact.affectsCapacity = true; break;
                case GeneCategoryTokens.Stat: fact.affectsStat = true; break;
            }
            return fact;
        }

        private static GrowthSkillFact Skill(string key, string label, string passion)
        {
            return new GrowthSkillFact { skillDefName = key, label = label, passion = passion };
        }

        private static FamilySupportCandidate Candidate(
            string id,
            string relation,
            int evidence,
            int tick,
            bool eligible,
            bool sameMap)
        {
            return new FamilySupportCandidate
            {
                adultId = id,
                displayName = id,
                relationToken = relation,
                lessonCount = evidence,
                unsummarizedEvidenceCount = evidence,
                lastObservedTick = tick,
                eligible = eligible,
                sameMap = sameMap
            };
        }

        private static FamilyParticipantFact Participant(string id, string name, bool eligible)
        {
            return new FamilyParticipantFact { pawnId = id, displayName = name, eligible = eligible };
        }

        private static BirthMutationSnapshot BirthSnapshot(string childId, int tick)
        {
            return new BirthMutationSnapshot
            {
                childId = childId,
                currentChildName = "Robin",
                birther = Participant("Birther", "Ari", true),
                geneticMother = Participant("Mother", "Bo", true),
                father = Participant("Father", "Cy", true),
                outcomeToken = BiotechBirthOutcomeTokens.Healthy,
                methodToken = BiotechBirthMethodTokens.Surrogacy,
                birthTick = tick,
                namingDeadline = tick + 100,
                namingResolved = false
            };
        }

        private static PendingBiotechBirthState PendingBirth(string childId, int tick)
        {
            BirthMutationSnapshot snapshot = BirthSnapshot(childId, tick);
            snapshot.familyArcId = "biotech-family|" + childId;
            return new PendingBiotechBirthState
            {
                snapshot = snapshot,
                writers = new BirthWriterSelection
                {
                    writers = new List<BirthWriterFact>
                    {
                        new BirthWriterFact
                        {
                            pawnId = "Birther_" + childId,
                            displayName = "Ari",
                            roleToken = BiotechFamilyRoleTokens.Birther
                        }
                    }
                },
                createdTick = tick
            };
        }

        private static BiotechFamilyArcState OpenBirthArc(string arcId, string hediffId, int tick)
        {
            return new BiotechFamilyArcState
            {
                familyArcId = arcId,
                pregnancyHediffId = hediffId,
                birtherId = "Birther",
                geneticMotherId = "Mother",
                fatherId = "Father",
                openedTick = tick,
                lastObservedTick = tick,
                supporters = new List<FamilySupportObservationState>(),
                recordedGrowthAges = new List<int>()
            };
        }

        private static XElement Group(XDocument document, string defName)
        {
            return document.Root.Elements("PawnDiary.DiaryInteractionGroupDef")
                .Single(element => Value(element, "defName") == defName);
        }

        private static string[] Values(XElement parent)
        {
            return parent == null
                ? new string[0]
                : parent.Elements("li").Select(element => element.Value.Trim()).ToArray();
        }

        private static string Value(XElement parent, string name)
        {
            XElement element = parent?.Element(name);
            return element == null ? string.Empty : element.Value.Trim();
        }

        private static string RepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "1.6"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Source")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new InvalidOperationException("Could not locate repository root.");
        }

        private static void AssertNotNull(string name, object value)
        {
            assertions++;
            if (value == null) throw new InvalidOperationException(name + ": expected non-null.");
        }

        private static void AssertTrue(string name, bool value)
        {
            assertions++;
            if (!value) throw new InvalidOperationException(name + ": expected true.");
        }

        private static void AssertContains(string name, string value, string expected)
        {
            assertions++;
            if (value == null || !value.Contains(expected))
            {
                throw new InvalidOperationException(name + ": expected to contain <" + expected + ">, got <" + value + ">.");
            }
        }

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(name + ": expected <" + expected + ">, got <" + actual + ">.");
            }
        }

        private static void AssertSequence<T>(string name, IEnumerable<T> expected, IEnumerable<T> actual)
        {
            assertions++;
            T[] expectedValues = expected == null ? new T[0] : expected.ToArray();
            T[] actualValues = actual == null ? new T[0] : actual.ToArray();
            if (!expectedValues.SequenceEqual(actualValues))
            {
                throw new InvalidOperationException(name + ": expected <" + string.Join(",", expectedValues)
                    + ">, got <" + string.Join(",", actualValues) + ">.");
            }
        }
    }
}

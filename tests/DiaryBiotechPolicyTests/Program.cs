// Standalone no-RimWorld tests for Master Wave 3 / Biotech B1. Besides exercising pure B1
// decisions, this suite parses the shipped XML/localization so stable IDs, package gates, band prose,
// and exact classifier ownership cannot drift independently of the contracts.
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
            TestShippedXmlPolicyAndLocalization();
            Console.WriteLine("DiaryBiotechPolicyTests passed " + assertions + " assertions.");
            return 0;
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
            AssertTrue("new-interest prompt prose is XML-owned",
                Value(def, "newInterestDescription").Length > 0);
            AssertTrue("deepened-interest prompt prose is XML-owned",
                Value(def, "deepenedInterestDescription").Length > 0);
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
            AssertEqual("growth group order", "800", Value(growth, "order"));
            AssertEqual("growth domain", "Progression", Value(growth, "domain"));
            AssertSequence("growth exact classifier", new[] { "BiotechGrowthMoment" },
                Values(growth.Element("matchDefNames")));
            AssertEqual("birth group order", "315", Value(birth, "order"));
            AssertEqual("birth domain", "Tale", Value(birth, "domain"));
            AssertSequence("birth exact classifier", new[] { "BiotechFamilyBirth" },
                Values(birth.Element("matchDefNames")));
            AssertSequence("growth package gate", new[] { "Ludeon.RimWorld.Biotech" },
                Values(growth.Element("enableWhenPackageIdsLoaded")));
            AssertSequence("birth package gate", new[] { "Ludeon.RimWorld.Biotech" },
                Values(birth.Element("enableWhenPackageIdsLoaded")));
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
                "biotechFamilyBirth.tones.0", "biotechFamilyBirth.tones.1"
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
                if (File.Exists(Path.Combine(directory.FullName, "BIOTECH_SUPPORT_IMPLEMENTATION_PLAN.md")))
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

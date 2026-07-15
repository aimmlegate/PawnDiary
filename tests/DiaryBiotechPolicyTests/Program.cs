// Standalone no-RimWorld tests for Master Wave 3 / Biotech Phase 0. Besides exercising pure B1
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
            TestSupporterSelectionAndWriterShapes();
            TestBirthWriterSelection();
            TestSettingsInheritance();
            TestContextFormatting();
            TestShippedXmlPolicyAndLocalization();
            Console.WriteLine("DiaryBiotechPolicyTests passed " + assertions + " assertions.");
            return 0;
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
                BiotechFamilyRoleTokens.Child, BiotechFamilyRoleTokens.Teacher);
            AssertContains("growth marker", context, "growth_moment=true");
            AssertContains("growth sanitized child id", context, "child_id=Child,1");
            AssertContains("growth opportunity prose", context, "opportunity_description=a mixed range");
            AssertContains("growth trait", context, "selected_trait=tough");
            AssertContains("growth sanitized trait description", context, "selected_trait_description=line one, line two");
            AssertContains("new interest wording", context, "interest_change_1=new interest");
            AssertContains("deepened interest wording", context, "interest_change_2=deepened interest");
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
            AssertEqual("policy supporter minimum", "2", Value(def, "supporterMinimumEvidence"));
            AssertTrue("policy exact BabyPlay matcher",
                Values(def.Element("familyActivityExactDefNames")).Contains("BabyPlay"));
            AssertTrue("policy Lesson prefix",
                Values(def.Element("familyActivityPrefixes")).Contains("Lesson"));

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
                "PawnDiary.Event.Biotech.Growth.SupporterFallback",
                "PawnDiary.Event.Biotech.Birth.Fallback",
                "PawnDiary.Event.Biotech.Birth.PairFallback"
            };
            foreach (string key in keyedNames)
            {
                AssertTrue(language + " keyed fallback " + key, Value(keyed.Root, key).Length > 0);
            }
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
                lastObservedTick = tick,
                eligible = eligible,
                sameMap = sameMap
            };
        }

        private static FamilyParticipantFact Participant(string id, string name, bool eligible)
        {
            return new FamilyParticipantFact { pawnId = id, displayName = name, eligible = eligible };
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

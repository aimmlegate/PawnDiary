// Standalone, no-RimWorld checks for the deterministic pawn-knowledge system
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §8): important-event classification against the shipped
// XML allowlist (positives, owners, dedup, and explicit negatives), localized line rendering with
// fallbacks, deterministic retrieval (participant/exact-key eligibility, tier ranking, stable
// ties, the two-record cap, and proof that broad topic overlap alone can never recall), culture
// resolution (ideology/faction paths, legacy inference, conversion replacement, unknown
// cultures), field-aware topic annotation (caps, origin/adopted, master switch, recursion
// prevention), defensive-cap eviction planning, and shipped-XML contract checks incl. Russian
// parity. The project links only pure source, so any accidental Verse/Unity dependency is a
// compile-time failure.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PawnDiary;

namespace PawnMemoryTests
{
    internal static class Program
    {
        private static int assertions;
        private static List<ImportantEventRule> shippedRules;

        private static int Main()
        {
            shippedRules = LoadShippedImportantEventRules();

            TestPolicyDefaultsAndXmlParity();
            TestSentinelValues();
            TestClassifierPositiveCatalog();
            TestClassifierNegativeCatalog();
            TestClassifierOwnersAndParticipants();
            TestClassifierBirthChildIdentity();
            TestClassifierContextGatesAndSubjects();
            TestIdentityPrefilter();
            TestClassifierDedupDeterminism();
            TestClassifierFirstMatchOrder();
            TestLineRendererTemplatesAndFallback();
            TestComposeBlockCaps();
            TestSelectorEligibilityDoors();
            TestSelectorRankingAndStableTies();
            TestSelectorTwoRecordCapAndReports();
            TestSelectorSelfEcho();
            TestSelectorBroadTopicNeverRecalls();
            TestQueryBuildFromRulesAndPolicy();
            TestEvictionPerPawnCap();
            TestEvictionGlobalCapAbsentFirst();
            TestEvictionDeterminismAndNoMutation();
            TestCultureResolutionPaths();
            TestCultureLegacyInferenceAndStability();
            TestCultureConversionReplacement();
            TestFamilyRelationDirection();
            TestAnnotationTopicDetectionPerField();
            TestAnnotationCapsAndPriority();
            TestAnnotationOriginAdoptedRendering();
            TestAnnotationMasterSwitchAndScannableSources();
            TestAnnotationRecursionPrevention();
            TestShippedCatalogContract();
            TestShippedCultureContractAndRussianParity();

            Console.WriteLine("PawnMemoryTests passed " + assertions + " assertions.");
            return 0;
        }

        // ── Shipped-XML loading (the same rows the game loads, minus Verse) ─────────────────────────

        private static string RepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "1.6", "Defs", "DiaryImportantEventDefs.xml")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
            {
                throw new InvalidOperationException("repo root with 1.6/Defs not found");
            }

            return dir.FullName;
        }

        private static List<ImportantEventRule> LoadShippedImportantEventRules()
        {
            string path = Path.Combine(RepoRoot(), "1.6", "Defs", "DiaryImportantEventDefs.xml");
            XDocument document = XDocument.Load(path);
            List<ImportantEventRule> rules = new List<ImportantEventRule>();
            foreach (XElement def in document.Root.Elements("PawnDiary.DiaryImportantEventDef"))
            {
                ImportantEventRule rule = new ImportantEventRule
                {
                    defName = (string)def.Element("defName") ?? string.Empty,
                    eventKind = (string)def.Element("eventKind") ?? string.Empty,
                    topicKey = (string)def.Element("topicKey") ?? string.Empty,
                    signal = (string)def.Element("signal") ?? KnowledgeTokens.SignalEvent,
                    owners = (string)def.Element("owners") ?? KnowledgeTokens.OwnersBoth,
                    lineTemplate = (string)def.Element("lineTemplate") ?? string.Empty
                };
                int order;
                if (int.TryParse((string)def.Element("order"), out order))
                {
                    rule.order = order;
                }

                rule.matchDefNames.AddRange(ListItems(def, "matchDefNames"));
                rule.matchSuffixes.AddRange(ListItems(def, "matchSuffixes"));
                rule.requireContext.AddRange(ListItems(def, "requireContext"));
                rule.constantSubjectKeys.AddRange(ListItems(def, "constantSubjectKeys"));
                rule.factKeys.AddRange(ListItems(def, "factKeys"));
                XElement subjectKeys = def.Element("subjectKeys");
                if (subjectKeys != null)
                {
                    foreach (XElement row in subjectKeys.Elements("li"))
                    {
                        rule.subjectKeyRules.Add(new KnowledgeSubjectKeyRule
                        {
                            contextKey = (string)row.Element("contextKey") ?? string.Empty,
                            prefix = (string)row.Element("prefix") ?? string.Empty
                        });
                    }
                }

                XElement participantKeys = def.Element("participantKeys");
                if (participantKeys != null)
                {
                    foreach (XElement row in participantKeys.Elements("li"))
                    {
                        rule.participantKeyRules.Add(new KnowledgeParticipantKeyRule
                        {
                            contextKey = (string)row.Element("contextKey") ?? string.Empty,
                            nameContextKey = (string)row.Element("nameContextKey") ?? string.Empty
                        });
                    }
                }

                rules.Add(rule);
            }

            return rules;
        }

        private static IEnumerable<string> ListItems(XElement def, string name)
        {
            XElement list = def.Element(name);
            return list == null
                ? Enumerable.Empty<string>()
                : list.Elements("li").Select(item => item.Value);
        }

        private static KnowledgeCaptureSignal EventSignal(string defName, string context,
            string initiatorId = "P1", string recipientId = "", string eventId = "ev1", int tick = 1000)
        {
            return new KnowledgeCaptureSignal
            {
                signal = KnowledgeTokens.SignalEvent,
                defName = defName,
                sourceEventId = eventId,
                tick = tick,
                dateLabel = "5 Jugtide 5501",
                gameContext = context,
                initiatorPawnId = initiatorId,
                initiatorName = "Ada",
                recipientPawnId = recipientId,
                recipientName = recipientId.Length == 0 ? string.Empty : "Brik"
            };
        }

        private static List<ImportantMemoryDraft> Classify(KnowledgeCaptureSignal signal)
        {
            return ImportantEventClassifier.Classify(
                signal, shippedRules, KnowledgePolicySnapshot.CreateDefault());
        }

        // ── Policy surface ───────────────────────────────────────────────────────────────────────────

        private static void TestPolicyDefaultsAndXmlParity()
        {
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            AssertEqual("default.injection", true, policy.injectionEnabled);
            AssertEqual("default.perPawn", 512, policy.maxRecordsPerPawn);
            AssertEqual("default.global", 20000, policy.maxRecordsGlobal);
            AssertEqual("default.lines", 2, policy.relevantPastMaxLines);
            AssertEqual("default.topics", 2, policy.maxCultureTopicsPerPrompt);

            // Behavioral parity with the shipped XML (the tuning Def must mirror CreateDefault).
            string path = Path.Combine(RepoRoot(), "1.6", "Defs", "DiaryKnowledgeTuningDef.xml");
            XElement def = XDocument.Load(path).Root.Element("PawnDiary.DiaryKnowledgeTuningDef");
            AssertEqual("xml.perPawn", policy.maxRecordsPerPawn,
                int.Parse((string)def.Element("maxRecordsPerPawn")));
            AssertEqual("xml.global", policy.maxRecordsGlobal,
                int.Parse((string)def.Element("maxRecordsGlobal")));
            AssertEqual("xml.lines", policy.relevantPastMaxLines,
                int.Parse((string)def.Element("relevantPastMaxLines")));
            AssertEqual("xml.chars", policy.relevantPastMaxChars,
                int.Parse((string)def.Element("relevantPastMaxChars")));
            AssertEqual("xml.fallbackChars", policy.fallbackSummaryMaxChars,
                int.Parse((string)def.Element("fallbackSummaryMaxChars")));
            AssertEqual("xml.topics", policy.maxCultureTopicsPerPrompt,
                int.Parse((string)def.Element("maxCultureTopicsPerPrompt")));
            AssertEqual("xml.lineFormat", policy.relevantPastLineFormat,
                (string)def.Element("relevantPastLineFormat"));
            AssertEqual("xml.singleFormat", policy.annotationSingleFormat,
                (string)def.Element("annotationSingleFormat"));
            AssertEqual("xml.dualFormat", policy.annotationDualFormat,
                (string)def.Element("annotationDualFormat"));

            List<string> xmlSources = ListItems(def, "scannableSources").ToList();
            AssertEqual("xml.sources.count", policy.scannableSources.Count, xmlSources.Count);
            for (int i = 0; i < xmlSources.Count; i++)
            {
                AssertContains("xml.sources", policy.scannableSources, xmlSources[i]);
            }

            XElement queryKeys = def.Element("querySubjectKeys");
            int rowIndex = 0;
            foreach (XElement row in queryKeys.Elements("li"))
            {
                AssertEqual("xml.query." + rowIndex + ".key",
                    policy.querySubjectKeyRules[rowIndex].contextKey, (string)row.Element("contextKey"));
                AssertEqual("xml.query." + rowIndex + ".prefix",
                    policy.querySubjectKeyRules[rowIndex].prefix, (string)row.Element("prefix"));
                rowIndex++;
            }

            AssertEqual("xml.query.count", policy.querySubjectKeyRules.Count, rowIndex);
        }

        private static void TestSentinelValues()
        {
            AssertTrue("sentinel.blank", KnowledgeTokens.IsSentinelValue("  "));
            AssertTrue("sentinel.none", KnowledgeTokens.IsSentinelValue("none"));
            AssertTrue("sentinel.na", KnowledgeTokens.IsSentinelValue("N/A"));
            AssertTrue("sentinel.unknown", KnowledgeTokens.IsSentinelValue("Unknown"));
            AssertTrue("sentinel.real", !KnowledgeTokens.IsSentinelValue("Spouse"));
        }

        // ── Classification (§2.1): every allowed kind and explicit negatives ─────────────────────────

        private static void TestClassifierPositiveCatalog()
        {
            // (defName, context, expected kind) — one row per allowed EVENT-channel kind.
            var cases = new[]
            {
                new { def = "Lover", ctx = "romance=Lover; kind=lover", kind = "relation.lover.gained" },
                new { def = "Spouse", ctx = "romance=Spouse; kind=married", kind = "relation.spouse.gained" },
                new { def = "ExLover", ctx = "romance=ExLover; kind=breakup", kind = "relation.lover.lost" },
                new { def = "ExSpouse", ctx = "romance=ExSpouse; kind=divorce", kind = "relation.spouse.lost" },
                new { def = "BiotechPsychicBondFormed", ctx = "psychic_bond=formed", kind = "bond.psychic.formed" },
                new { def = "BiotechPsychicBondRuptured", ctx = "psychic_bond=ruptured", kind = "bond.psychic.ruptured" },
                new { def = "BiotechFamilyBirth", ctx = "biotech_birth=true; child_id=Pawn_9", kind = "family.birth" },
                new { def = "GaveBirth", ctx = "tale=GaveBirth", kind = "family.birth" },
                new { def = "SomeBionicArm", ctx = "hediff=BionicArm; label=bionic arm; part_kind=addedpart; part_def=Shoulder; body_part=left shoulder; part_tier=bionic", kind = "body.part.installed" },
                new { def = "SomeHeart", ctx = "hediff=Heart; label=heart; part_kind=organicpart; part_def=Heart; body_part=heart", kind = "body.part.installed" },
                new { def = "MissingLeg", ctx = "hediff=MissingBodyPart; label=missing leg; part_kind=missingpart; part_def=Leg; body_part=left leg; part_cause=violence", kind = "body.part.lost" },
                new { def = "PawnDiary_Arrival", ctx = "arrival_description=true; arrival_source=recruited", kind = "status.faction.joined" },
                new { def = "BiotechGrowthMoment", ctx = "growth_moment=true; growth_stage=13", kind = "status.growth" },
                new { def = "RoyalTitleGained", ctx = "progression=RoyalTitleGained; new_value=Knight", kind = "status.title.advanced" },
                new { def = "RoyalTitlePromoted", ctx = "progression=RoyalTitlePromoted; previous_value=Knight; new_value=Praetor", kind = "status.title.advanced" },
                new { def = "RoyalTitleDemoted", ctx = "progression=RoyalTitleDemoted; previous_value=Praetor; new_value=Knight", kind = "status.title.lost" },
                new { def = "RoyalTitleLost", ctx = "progression=RoyalTitleLost; previous_value=Knight", kind = "status.title.lost" },
                new { def = "PsylinkLevel", ctx = "progression=PsylinkLevel; previous_value=1; new_value=2", kind = "status.psylink" },
                new { def = "XenotypeChanged", ctx = "progression=XenotypeChanged; previous_value=Baseliner; new_value=Hussar", kind = "body.xenotype" },
                new { def = "GeneIdentityChanged", ctx = "progression=GeneIdentityChanged", kind = "body.genes" },
                new { def = "BiotechMechlinkInstalled", ctx = "progression=BiotechMechlinkInstalled", kind = "status.mechlink.gained" },
                new { def = "BiotechMechlinkRemoved", ctx = "progression=BiotechMechlinkRemoved", kind = "status.mechlink.lost" },
                new { def = "PersonaWeaponBondFormed", ctx = "persona_weapon=true; persona_weapon_id=Thing_42; persona_weapon_name=Whisper", kind = "bond.persona.formed" },
                new { def = "PersonaWeaponBondEnded", ctx = "persona_weapon=true; persona_weapon_id=Thing_42; persona_weapon_name=Whisper", kind = "bond.persona.ended" },
            };
            foreach (var entry in cases)
            {
                List<ImportantMemoryDraft> drafts = Classify(EventSignal(entry.def, entry.ctx));
                AssertTrue("positive." + entry.def + ".matched", drafts.Count > 0);
                AssertEqual("positive." + entry.def + ".kind", entry.kind, drafts[0].record.eventKind);
            }

            // Non-event channels: quiet hediff allowlist, removal, roles, conversion, death fan-out.
            var channelCases = new[]
            {
                new { signal = KnowledgeTokens.SignalHediffQuiet, def = "LuciferiumAddiction", ctx = "hediff=LuciferiumAddiction; label=luciferium need", kind = "body.condition.permanent" },
                new { signal = KnowledgeTokens.SignalHediffQuiet, def = "Sterilized", ctx = "hediff=Sterilized; label=sterilized", kind = "body.condition.permanent" },
                new { signal = KnowledgeTokens.SignalHediffRemoved, def = "BionicArm_addedpart", ctx = "hediff=BionicArm; label=bionic arm; part_def=Shoulder; body_part=left shoulder", kind = "body.part.removed" },
                new { signal = KnowledgeTokens.SignalRoleAssigned, def = "PawnDiary_RoleAssigned", ctx = "role=moral guide; ideo=The Flame", kind = "status.role.gained" },
                new { signal = KnowledgeTokens.SignalRoleUnassigned, def = "PawnDiary_RoleUnassigned", ctx = "role=moral guide; ideo=The Flame", kind = "status.role.lost" },
                new { signal = KnowledgeTokens.SignalIdeoConversion, def = "PawnDiary_IdeoConversion", ctx = "previous_ideo=Old Way; new_ideo=The Flame; new_culture=Corunan", kind = "status.ideo.converted" },
                new { signal = KnowledgeTokens.SignalDeathFamily, def = "PawnDiary_DeathFamily", ctx = "victim=Brik; relation=husband", kind = "death.family" },
                new { signal = KnowledgeTokens.SignalDeathInstigator, def = "PawnDiary_DeathInstigator", ctx = "victim=Raider; weapon=knife", kind = "death.killed" },
            };
            foreach (var entry in channelCases)
            {
                KnowledgeCaptureSignal signal = EventSignal(entry.def, entry.ctx);
                signal.signal = entry.signal;
                signal.providedOwnerPawnId = "P1";
                List<ImportantMemoryDraft> drafts = Classify(signal);
                AssertTrue("channel." + entry.def + ".matched", drafts.Count > 0);
                AssertEqual("channel." + entry.def + ".kind", entry.kind, drafts[0].record.eventKind);
                AssertEqual("channel." + entry.def + ".owner", "P1", drafts[0].ownerPawnId);
            }
        }

        private static void TestClassifierNegativeCatalog()
        {
            // §2.1 exclusions: routine social/quest/skill/mental signals must never draft records.
            string[] negatives =
            {
                "Chat", "DeepTalk", "Insult", "SkillMilestone", "TraitGained", "ProgressionOther",
                "SocialFighting", "Wander_Sad", "PawnDiary_WorkPassion", "PawnDiary_DeathFallback",
                "RoyalSuccession", "RoyalHeirAppointed", "PersonaWeaponBondSeparated",
                "PersonaWeaponBondRecovered"
            };
            foreach (string defName in negatives)
            {
                AssertEqual("negative." + defName, 0,
                    Classify(EventSignal(defName, "thought=" + defName)).Count);
            }

            // A hediff event WITHOUT a structural part token stays ignored (ordinary scars, flu…).
            AssertEqual("negative.plainHediff", 0,
                Classify(EventSignal("Flu", "hediff=Flu; label=flu; severity=0.4")).Count);

            // The quiet channel is a strict allowlist: an unlisted hediff never drafts.
            KnowledgeCaptureSignal quiet = EventSignal("Carcinoma", "hediff=Carcinoma; label=carcinoma");
            quiet.signal = KnowledgeTokens.SignalHediffQuiet;
            quiet.providedOwnerPawnId = "P1";
            AssertEqual("negative.quietUnlisted", 0, Classify(quiet).Count);
        }

        private static void TestClassifierOwnersAndParticipants()
        {
            // Marriage: BOTH pawns own a record; each references the other as participant (§2.1).
            List<ImportantMemoryDraft> drafts = Classify(
                EventSignal("Spouse", "romance=Spouse; kind=married", "P1", "P2"));
            AssertEqual("owners.marriage.count", 2, drafts.Count);
            AssertEqual("owners.marriage.first", "P1", drafts[0].ownerPawnId);
            AssertEqual("owners.marriage.second", "P2", drafts[1].ownerPawnId);
            AssertEqual("owners.marriage.p1.other", "P2", drafts[0].record.participants[0].pawnId);
            AssertEqual("owners.marriage.p2.other", "P1", drafts[1].record.participants[0].pawnId);
            AssertEqual("owners.marriage.p2.otherName", "Ada", drafts[1].record.participants[0].name);

            // Body events: initiator only, even on a pair signal.
            drafts = Classify(EventSignal("MissingLeg",
                "hediff=MissingBodyPart; part_kind=missingpart; part_def=Leg; body_part=leg", "P1", "P2"));
            AssertEqual("owners.body.count", 1, drafts.Count);
            AssertEqual("owners.body.owner", "P1", drafts[0].ownerPawnId);

            // Death fan-out: provided owner + victim as extra participant.
            KnowledgeCaptureSignal death = EventSignal("PawnDiary_DeathFamily",
                "victim=Brik; relation=husband");
            death.signal = KnowledgeTokens.SignalDeathFamily;
            death.providedOwnerPawnId = "P7";
            death.extraParticipants.Add(new KnowledgeParticipant { pawnId = "P2", name = "Brik" });
            drafts = Classify(death);
            AssertEqual("owners.death.count", 1, drafts.Count);
            AssertEqual("owners.death.owner", "P7", drafts[0].ownerPawnId);
            AssertContains("owners.death.participant",
                drafts[0].record.participants.Select(p => p.pawnId).ToList(), "P2");

            // Blank owners never draft.
            drafts = Classify(EventSignal("Spouse", "romance=Spouse", "", ""));
            AssertEqual("owners.blank", 0, drafts.Count);
        }

        private static void TestClassifierBirthChildIdentity()
        {
            KnowledgeCaptureSignal first = EventSignal(
                "BiotechFamilyBirth",
                "biotech_birth=true; child_id=Pawn_Child1; child_name=Mira",
                "P1",
                "P2",
                tick: 4000);
            List<ImportantMemoryDraft> drafts = Classify(first);
            AssertEqual("birthIdentity.ownerCount", 2, drafts.Count);
            for (int i = 0; i < drafts.Count; i++)
            {
                AssertContains(
                    "birthIdentity.subject." + i,
                    drafts[i].record.subjectKeys,
                    "pawn:Pawn_Child1");
                KnowledgeParticipant child = drafts[i].record.participants.First(
                    participant => participant.pawnId == "Pawn_Child1");
                AssertEqual("birthIdentity.name." + i, "Mira", child.name);
            }

            ImportantMemoryDraft second = Classify(EventSignal(
                "BiotechFamilyBirth",
                "biotech_birth=true; child_id=Pawn_Child2; child_name=Niko",
                "P1",
                "P2",
                tick: 4000))[0];
            AssertTrue(
                "birthIdentity.twinsDistinct",
                drafts[0].record.dedupKey != second.record.dedupKey);
        }

        private static void TestClassifierContextGatesAndSubjects()
        {
            // requireContext "key=value" gates: the install row extracts subject and fact rows.
            List<ImportantMemoryDraft> drafts = Classify(EventSignal("BionicArm",
                "hediff=BionicArm; label=bionic arm; part_kind=addedpart; part_def=Shoulder; body_part=left shoulder; part_tier=bionic"));
            AssertEqual("gates.install.count", 1, drafts.Count);
            ImportantMemoryRecordSnapshot record = drafts[0].record;
            AssertContains("gates.install.subject.part", record.subjectKeys, "part:Shoulder");
            AssertContains("gates.install.subject.implant", record.subjectKeys, "implant:BionicArm");
            AssertEqual("gates.install.fact.label", "bionic arm",
                record.facts.First(f => f.key == "label").value);
            AssertEqual("gates.install.fact.tier", "bionic",
                record.facts.First(f => f.key == "part_tier").value);
            AssertEqual("gates.install.date", "5 Jugtide 5501", record.dateLabel);

            // Sentinel context values are never subjects or facts.
            drafts = Classify(EventSignal("MissingLeg",
                "hediff=MissingBodyPart; part_kind=missingpart; part_def=none; body_part=unknown; part_cause=violence"));
            AssertEqual("gates.sentinel.count", 1, drafts.Count);
            AssertEqual("gates.sentinel.subjects", 0, drafts[0].record.subjectKeys.Count);
            AssertTrue("gates.sentinel.facts",
                drafts[0].record.facts.All(f => f.key != "body_part"));

            // Constant subject keys: every title row carries the "title" family key (§3.1).
            drafts = Classify(EventSignal("RoyalTitleGained",
                "progression=RoyalTitleGained; new_value=Knight"));
            AssertContains("gates.title.constant", drafts[0].record.subjectKeys, "title");
        }

        private static void TestIdentityPrefilter()
        {
            AssertTrue("prefilter.quiet.hit", ImportantEventClassifier.MayMatchIdentity(
                KnowledgeTokens.SignalHediffQuiet, "LuciferiumAddiction", shippedRules));
            AssertTrue("prefilter.quiet.miss", !ImportantEventClassifier.MayMatchIdentity(
                KnowledgeTokens.SignalHediffQuiet, "Cut", shippedRules));
            AssertTrue("prefilter.removed.suffix", ImportantEventClassifier.MayMatchIdentity(
                KnowledgeTokens.SignalHediffRemoved, "BionicArm_addedpart", shippedRules));
            AssertTrue("prefilter.event.contextOnly", ImportantEventClassifier.MayMatchIdentity(
                KnowledgeTokens.SignalEvent, "UnlistedBodyDef", shippedRules));
        }

        private static void TestClassifierDedupDeterminism()
        {
            KnowledgeCaptureSignal signal = EventSignal("Spouse", "romance=Spouse; kind=married", "P1", "P2");
            ImportantMemoryDraft first = Classify(signal)[0];
            ImportantMemoryDraft second = Classify(signal)[0];
            AssertEqual("dedup.stable", first.record.dedupKey, second.record.dedupKey);
            AssertEqual("dedup.recordId", first.record.recordId, second.record.recordId);
            AssertTrue("dedup.ownerScoped", first.record.dedupKey.StartsWith("P1|", StringComparison.Ordinal));

            // Different tick ⇒ a different record identity (losing the same part twice is real).
            KnowledgeCaptureSignal later = EventSignal("Spouse", "romance=Spouse; kind=married", "P1", "P2");
            later.tick = 2000;
            AssertTrue("dedup.tickDistinct",
                first.record.dedupKey != Classify(later)[0].record.dedupKey);
        }

        private static void TestClassifierFirstMatchOrder()
        {
            // Two rules match; the lower order (then ordinal defName) wins, deterministically.
            List<ImportantEventRule> rules = new List<ImportantEventRule>
            {
                new ImportantEventRule { defName = "B", eventKind = "k.b", order = 10, matchDefNames = { "X" } },
                new ImportantEventRule { defName = "A", eventKind = "k.a", order = 10, matchDefNames = { "X" } },
                new ImportantEventRule { defName = "C", eventKind = "k.c", order = 5, matchDefNames = { "X" } },
            };
            KnowledgeCaptureSignal signal = EventSignal("X", string.Empty);
            ImportantEventRule match = ImportantEventClassifier.FirstMatch(signal, rules);
            AssertEqual("order.winner", "C", match.defName);
            rules.RemoveAt(2);
            AssertEqual("order.tieOrdinal", "A",
                ImportantEventClassifier.FirstMatch(signal, rules).defName);
        }

        // ── Rendering (§3.2) ─────────────────────────────────────────────────────────────────────────

        private static void TestLineRendererTemplatesAndFallback()
        {
            ImportantMemoryRecordSnapshot record = new ImportantMemoryRecordSnapshot();
            record.participants.Add(new KnowledgeParticipant { pawnId = "P2", name = "Brik" });
            record.facts.Add(new KnowledgeFact { key = "body_part", value = "left leg" });
            record.fallbackSummary = "captured fallback";

            AssertEqual("render.other", "married Brik",
                ImportantMemoryLineRenderer.Render(record, "married {other}", 240));
            AssertEqual("render.fact", "lost left leg",
                ImportantMemoryLineRenderer.Render(record, "lost {body_part}", 240));
            // Unresolved placeholders strip cleanly — no braces may leak into a prompt.
            AssertEqual("render.unresolved", "became",
                ImportantMemoryLineRenderer.Render(record, "became {new_value}", 240));
            AssertEqual("render.blankTemplate", "captured fallback",
                ImportantMemoryLineRenderer.Render(record, "  ", 240));
            AssertEqual("render.cap", "married",
                ImportantMemoryLineRenderer.Render(record, "married {other}", 8));
            AssertEqual("render.null", string.Empty,
                ImportantMemoryLineRenderer.Render(null, "x", 240));
        }

        private static void TestComposeBlockCaps()
        {
            List<string> lines = new List<string> { "- (d1) one", "- (d2) two", "- (d3) three" };
            AssertEqual("compose.lineCap", "- (d1) one\n- (d2) two",
                ImportantMemoryLineRenderer.ComposeBlock(lines, 2, 500));
            // Character budget drops WHOLE lines from the end, never truncating mid-fact.
            AssertEqual("compose.charBudget", "- (d1) one",
                ImportantMemoryLineRenderer.ComposeBlock(lines, 2, 15));
            AssertEqual("compose.empty", string.Empty,
                ImportantMemoryLineRenderer.ComposeBlock(new List<string>(), 2, 500));
        }

        // ── Retrieval (§3.1) ─────────────────────────────────────────────────────────────────────────

        private static ImportantMemoryRecordSnapshot Record(string id, int tick,
            string participantId = null, string subjectKey = null, string topicKey = null,
            string sourceEventId = null)
        {
            ImportantMemoryRecordSnapshot record = new ImportantMemoryRecordSnapshot
            {
                recordId = id,
                dedupKey = id,
                ownerPawnId = "P1",
                eventKind = "test.kind",
                topicKey = topicKey ?? string.Empty,
                tick = tick,
                sourceEventId = sourceEventId ?? string.Empty,
                fallbackSummary = id
            };
            if (participantId != null)
            {
                record.participants.Add(new KnowledgeParticipant { pawnId = participantId, name = participantId });
            }

            if (subjectKey != null)
            {
                record.subjectKeys.Add(subjectKey);
            }

            return record;
        }

        private static KnowledgeQuery Query(string participantId = null, string subjectKey = null,
            string topicKey = null, string eventId = "evQ")
        {
            KnowledgeQuery query = new KnowledgeQuery
            {
                eventId = eventId,
                ownerPawnId = "P1",
                currentTick = 9000
            };
            if (participantId != null)
            {
                query.participantIds.Add(participantId);
            }

            if (subjectKey != null)
            {
                query.subjectKeys.Add(subjectKey);
            }

            if (topicKey != null)
            {
                query.topicKeys.Add(topicKey);
            }

            return query;
        }

        private static void TestSelectorEligibilityDoors()
        {
            List<ImportantMemoryRecordSnapshot> records = new List<ImportantMemoryRecordSnapshot>
            {
                Record("byParticipant", 100, participantId: "P2"),
                Record("bySubject", 100, subjectKey: "part:Leg"),
                Record("unrelated", 100, participantId: "P9", subjectKey: "title")
            };
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();

            KnowledgeSelectionResult result = ImportantMemorySelector.Select(
                Query(participantId: "P2"), records, policy);
            AssertEqual("doors.participant.count", 1, result.selected.Count);
            AssertEqual("doors.participant.pick", "byParticipant", result.selected[0].recordId);

            result = ImportantMemorySelector.Select(Query(subjectKey: "part:Leg"), records, policy);
            AssertEqual("doors.subject.count", 1, result.selected.Count);
            AssertEqual("doors.subject.pick", "bySubject", result.selected[0].recordId);

            // Case-insensitive exact key matching, never substring.
            result = ImportantMemorySelector.Select(Query(subjectKey: "PART:LEG"), records, policy);
            AssertEqual("doors.subject.caseless", 1, result.selected.Count);
            result = ImportantMemorySelector.Select(Query(subjectKey: "part:Le"), records, policy);
            AssertEqual("doors.subject.noSubstring", 0, result.selected.Count);
        }

        private static void TestSelectorRankingAndStableTies()
        {
            List<ImportantMemoryRecordSnapshot> records = new List<ImportantMemoryRecordSnapshot>
            {
                Record("subjectNewer", 500, subjectKey: "part:Leg"),
                Record("participantOlder", 100, participantId: "P2"),
                Record("topicNewest", 900, subjectKey: "part:Leg", topicKey: "body"),
            };
            KnowledgeQuery query = Query(participantId: "P2", subjectKey: "part:Leg", topicKey: "body");
            KnowledgeSelectionResult result = ImportantMemorySelector.Select(
                query, records, KnowledgePolicySnapshot.CreateDefault());
            // Shared participant outranks exact key (§3.1 tier order), regardless of recency.
            AssertEqual("rank.first", "participantOlder", result.selected[0].recordId);
            // Among key-matches, the topic tier then newest tick decides.
            AssertEqual("rank.second", "topicNewest", result.selected[1].recordId);

            // Full tie ⇒ record-id ordinal, stable across runs.
            List<ImportantMemoryRecordSnapshot> ties = new List<ImportantMemoryRecordSnapshot>
            {
                Record("b", 100, subjectKey: "psylink"),
                Record("a", 100, subjectKey: "psylink"),
            };
            result = ImportantMemorySelector.Select(Query(subjectKey: "psylink"), ties,
                KnowledgePolicySnapshot.CreateDefault());
            AssertEqual("rank.tie", "a", result.selected[0].recordId);
        }

        private static void TestSelectorTwoRecordCapAndReports()
        {
            List<ImportantMemoryRecordSnapshot> records = new List<ImportantMemoryRecordSnapshot>();
            for (int i = 0; i < 5; i++)
            {
                records.Add(Record("r" + i, i * 100, participantId: "P2"));
            }

            KnowledgeSelectionResult result = ImportantMemorySelector.Select(
                Query(participantId: "P2"), records, KnowledgePolicySnapshot.CreateDefault());
            AssertEqual("cap.count", 2, result.selected.Count);
            AssertEqual("cap.newestFirst", "r4", result.selected[0].recordId);
            AssertEqual("cap.report.rows", 5, result.report.Count);
            AssertEqual("cap.report.overCap", 3,
                result.report.Count(r => r.rejectReason == KnowledgeRejectReasons.OverCap));
            AssertEqual("cap.report.selected", 2, result.report.Count(r => r.selected));
        }

        private static void TestSelectorSelfEcho()
        {
            List<ImportantMemoryRecordSnapshot> records = new List<ImportantMemoryRecordSnapshot>
            {
                Record("fromThisEvent", 100, participantId: "P2", sourceEventId: "evQ"),
                Record("older", 50, participantId: "P2", sourceEventId: "evOld"),
            };
            KnowledgeSelectionResult result = ImportantMemorySelector.Select(
                Query(participantId: "P2", eventId: "evQ"), records,
                KnowledgePolicySnapshot.CreateDefault());
            AssertEqual("selfEcho.count", 1, result.selected.Count);
            AssertEqual("selfEcho.pick", "older", result.selected[0].recordId);
            AssertEqual("selfEcho.reason", KnowledgeRejectReasons.SelfEcho,
                result.report.First(r => r.recordId == "fromThisEvent").rejectReason);
        }

        private static void TestSelectorBroadTopicNeverRecalls()
        {
            // §8 proof: broad mood/social/body/danger domains — modeled as topic-family overlap —
            // can NEVER recall a record by themselves. Only a concrete participant or an exact
            // subject key opens the door.
            List<ImportantMemoryRecordSnapshot> records = new List<ImportantMemoryRecordSnapshot>
            {
                Record("topicOnly", 100, topicKey: "body"),
            };
            KnowledgeSelectionResult result = ImportantMemorySelector.Select(
                Query(topicKey: "body"), records, KnowledgePolicySnapshot.CreateDefault());
            AssertEqual("broad.count", 0, result.selected.Count);
            AssertEqual("broad.reason", KnowledgeRejectReasons.NoOverlap,
                result.report[0].rejectReason);
        }

        private static void TestQueryBuildFromRulesAndPolicy()
        {
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            KnowledgeQuery query = ImportantMemorySelector.BuildQuery(
                "ev9", "P1", "P2", 5000,
                "romance=Spouse; kind=married; weapon=knife; royal_title=none",
                "Spouse", shippedRules, policy);
            AssertContains("query.participant", query.participantIds, "P2");
            AssertContains("query.subject.relation", query.subjectKeys, "relation:Spouse");
            AssertContains("query.subject.weapon", query.subjectKeys, "weapon:knife");
            // Sentinel values never become probes.
            AssertTrue("query.sentinel", query.subjectKeys.All(k => !k.StartsWith("title:")));
            // The classified rule contributes its topic family (ranking tier 3).
            AssertContains("query.topic", query.topicKeys, "relationship");

            // A title event contributes the constant "title" family probe via its matched rule.
            query = ImportantMemorySelector.BuildQuery(
                "ev10", "P1", null, 5000,
                "progression=RoyalTitlePromoted; previous_value=Knight; new_value=Praetor",
                "RoyalTitlePromoted", shippedRules, policy);
            AssertContains("query.title.constant", query.subjectKeys, "title");
        }

        // ── Defensive caps (§2.3) ────────────────────────────────────────────────────────────────────

        private static KnowledgeOwnerLoad Owner(string id, bool absent, params int[] ticks)
        {
            KnowledgeOwnerLoad load = new KnowledgeOwnerLoad { ownerPawnId = id, ownerAbsent = absent };
            for (int i = 0; i < ticks.Length; i++)
            {
                load.records.Add(new KnowledgeRecordStub { recordId = id + "-" + i, tick = ticks[i] });
            }

            return load;
        }

        private static void TestEvictionPerPawnCap()
        {
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            policy.maxRecordsPerPawn = 2;
            List<KnowledgeOwnerLoad> owners = new List<KnowledgeOwnerLoad>
            {
                Owner("A", false, 300, 100, 200)
            };
            KnowledgeEvictionPlan plan = KnowledgeEvictionPlanner.Plan(owners, policy);
            AssertEqual("evict.perPawn.count", 1, plan.dropRecordIds.Count);
            AssertEqual("evict.perPawn.oldest", "A-1", plan.dropRecordIds[0]);
            AssertTrue("evict.perPawn.noWarn", !plan.globalCapHit);
        }

        private static void TestEvictionGlobalCapAbsentFirst()
        {
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            policy.maxRecordsPerPawn = 10;
            policy.maxRecordsGlobal = 3;
            List<KnowledgeOwnerLoad> owners = new List<KnowledgeOwnerLoad>
            {
                Owner("present", false, 10, 20),
                Owner("absent", true, 500, 600)
            };
            KnowledgeEvictionPlan plan = KnowledgeEvictionPlanner.Plan(owners, policy);
            AssertEqual("evict.global.count", 1, plan.dropRecordIds.Count);
            // The ABSENT owner's oldest record goes first even though the present owner's records
            // are older (§2.3), and the one bounded warning is requested.
            AssertEqual("evict.global.absentFirst", "absent-0", plan.dropRecordIds[0]);
            AssertTrue("evict.global.warn", plan.globalCapHit);
        }

        private static void TestEvictionDeterminismAndNoMutation()
        {
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            policy.maxRecordsPerPawn = 1;
            policy.maxRecordsGlobal = 1;
            List<KnowledgeOwnerLoad> owners = new List<KnowledgeOwnerLoad>
            {
                Owner("A", false, 100, 100),
                Owner("B", false, 100)
            };
            KnowledgeEvictionPlan first = KnowledgeEvictionPlanner.Plan(owners, policy);
            KnowledgeEvictionPlan second = KnowledgeEvictionPlanner.Plan(owners, policy);
            AssertEqual("evict.deterministic", string.Join(",", first.dropRecordIds),
                string.Join(",", second.dropRecordIds));
            AssertEqual("evict.noMutation", 2, owners[0].records.Count);
        }

        // ── Culture (§4.1) ───────────────────────────────────────────────────────────────────────────

        private static void TestCultureResolutionPaths()
        {
            // Ideology active ⇒ ideology culture wins.
            CultureStateSnapshot state = CultureResolver.ResolveOrigin(new CultureResolutionInput
            {
                ideologyActive = true,
                ideoCultureDefName = "Corunan",
                factionCultureDefNames = new List<string> { "Astropolitan" }
            });
            AssertEqual("culture.ideo", "Corunan", state.originCultureDefName);
            AssertEqual("culture.ideo.source", KnowledgeTokens.CultureSourceCaptured, state.originSource);

            // An origin-boundary snapshot outranks mutable post-arrival/current ideology state.
            state = CultureResolver.ResolveOrigin(new CultureResolutionInput
            {
                capturedOriginCultureDefName = "Rustican",
                ideologyActive = true,
                ideoCultureDefName = "Corunan",
                factionCultureDefNames = new List<string> { "Astropolitan" }
            });
            AssertEqual("culture.capturedBoundary", "Rustican", state.originCultureDefName);

            // Without Ideology ⇒ the faction's FIRST allowed culture (deterministic).
            state = CultureResolver.ResolveOrigin(new CultureResolutionInput
            {
                ideologyActive = false,
                ideoCultureDefName = "Corunan",
                factionCultureDefNames = new List<string> { "", "Rustican", "Kriminul" }
            });
            AssertEqual("culture.faction", "Rustican", state.originCultureDefName);

            // Nothing resolvable ⇒ EMPTY, never an invented fallback (§4.1).
            state = CultureResolver.ResolveOrigin(new CultureResolutionInput { ideologyActive = false });
            AssertEqual("culture.unknown", string.Empty, state.originCultureDefName);
            AssertEqual("culture.unknown.source", string.Empty, state.originSource);
        }

        private static void TestCultureLegacyInferenceAndStability()
        {
            CultureStateSnapshot state = CultureResolver.ResolveOrigin(new CultureResolutionInput
            {
                ideologyActive = true,
                ideoCultureDefName = "Sophian",
                legacyInference = true
            });
            AssertEqual("legacy.source", KnowledgeTokens.CultureSourceInferred, state.originSource);

            // A resolved origin is never silently rewritten later (§4.1).
            AssertTrue("legacy.needs.blank",
                CultureResolver.NeedsOriginResolution(new CultureStateSnapshot()));
            AssertTrue("legacy.needs.resolved",
                !CultureResolver.NeedsOriginResolution(state));
        }

        private static void TestCultureConversionReplacement()
        {
            CultureStateSnapshot state = new CultureStateSnapshot
            {
                originCultureDefName = "Rustican",
                originSource = KnowledgeTokens.CultureSourceCaptured
            };
            state = CultureResolver.ApplyConversion(state, "Corunan");
            AssertEqual("convert.first", "Corunan", state.adoptedCultureDefName);
            // A second conversion REPLACES the adopted culture; earlier ones are not retained.
            state = CultureResolver.ApplyConversion(state, "Astropolitan");
            AssertEqual("convert.replace", "Astropolitan", state.adoptedCultureDefName);
            AssertEqual("convert.originUntouched", "Rustican", state.originCultureDefName);
            // Blank conversions change nothing; effective culture prefers adopted.
            state = CultureResolver.ApplyConversion(state, "  ");
            AssertEqual("convert.blankNoop", "Astropolitan", state.adoptedCultureDefName);
            AssertEqual("convert.effective", "Astropolitan", CultureResolver.EffectiveCulture(state));
            AssertEqual("convert.effectiveOrigin", "Rustican",
                CultureResolver.EffectiveCulture(new CultureStateSnapshot { originCultureDefName = "Rustican" }));
        }

        private static void TestFamilyRelationDirection()
        {
            AssertEqual(
                "relation.parentToChild",
                "Child",
                KnowledgeRelationPolicy.VictimRelationDefName("Parent"));
            AssertEqual(
                "relation.childToParent",
                "Parent",
                KnowledgeRelationPolicy.VictimRelationDefName("Child"));
            AssertEqual(
                "relation.spouseStable",
                "Spouse",
                KnowledgeRelationPolicy.VictimRelationDefName("Spouse"));
            AssertEqual(
                "relation.deathFanoutAllowsLastSlot",
                true,
                KnowledgeRelationPolicy.CanEmitDeathFamilyOwner(
                    KnowledgeRelationPolicy.MaximumDeathFamilyOwners - 1));
            AssertEqual(
                "relation.deathFanoutStopsAtCap",
                false,
                KnowledgeRelationPolicy.CanEmitDeathFamilyOwner(
                    KnowledgeRelationPolicy.MaximumDeathFamilyOwners));
            AssertEqual(
                "relation.deathFanoutRejectsMalformedCount",
                false,
                KnowledgeRelationPolicy.CanEmitDeathFamilyOwner(-1));
        }

        // ── Inline annotation (§4.3) ─────────────────────────────────────────────────────────────────

        private static CultureProfile Profile(string culture, params string[] topicClausePairs)
        {
            CultureProfile profile = new CultureProfile { cultureDefName = culture };
            for (int i = 0; i + 1 < topicClausePairs.Length; i += 2)
            {
                profile.clauses.Add(new CultureClause
                {
                    topicKey = topicClausePairs[i],
                    clause = topicClausePairs[i + 1]
                });
            }

            return profile;
        }

        private static CultureTopicRule Topic(string key, int order)
        {
            return new CultureTopicRule { topicKey = key, order = order };
        }

        private static AnnotationFieldView Field(int index, string source, string value,
            string contextKey = "")
        {
            return new AnnotationFieldView
            {
                index = index,
                source = source,
                contextKey = contextKey,
                resolvedValue = value
            };
        }

        private static void TestAnnotationTopicDetectionPerField()
        {
            CultureTopicRule byKey = Topic("psychic", 10);
            byKey.triggerContextKeys.Add("psychic_ritual");
            CultureTopicRule byPair = Topic("archotech", 20);
            byPair.triggerContextPairs.Add("part_tier=archotech");
            CultureTopicRule byMarker = Topic("xenohumans", 30);
            byMarker.triggerValueMarkers.Add("xenotype=");
            CultureTopicRule byDefName = Topic("empire", 40);
            byDefName.triggerDefNames.Add("RoyalTitleGained");
            List<CultureTopicRule> topics = new List<CultureTopicRule> { byKey, byPair, byMarker, byDefName };
            CultureProfile profile = Profile("Rustican",
                "psychic", "strange weather", "archotech", "ended story",
                "xenohumans", "tool fixed", "empire", "orbit law");
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            policy.maxCultureTopicsPerPrompt = 1;

            // Context-KEY trigger: only a GameContext field with that exact contextKey fires.
            CultureAnnotationPlan plan = CultureAnnotationPlanner.Plan(
                new List<AnnotationFieldView>
                {
                    Field(0, "PovText", "a strange dance"),
                    Field(1, "GameContext", "solar pilgrimage", "psychic_ritual")
                },
                "SomeRitual", topics, profile, null, policy);
            AssertEqual("detect.key.count", 1, plan.entries.Count);
            AssertEqual("detect.key.field", 1, plan.entries[0].fieldIndex);
            AssertEqual("detect.key.text", "(culture: strange weather)", plan.entries[0].text);

            // The template may display one GameContext key while another stable key in the same
            // selected event context owns the topic.
            AnnotationFieldView structured = Field(5, "GameContext", "ritual complete", "label");
            structured.structuredContext = "label=ritual complete; psychic_ritual=stormcalling";
            plan = CultureAnnotationPlanner.Plan(
                new List<AnnotationFieldView> { structured },
                "SomeRitual", topics, profile, null, policy);
            AssertEqual("detect.structuredKey.topic", "psychic", plan.matchedTopics[0]);
            structured.structuredContext = "label=implant; part_tier=archotech";
            plan = CultureAnnotationPlanner.Plan(
                new List<AnnotationFieldView> { structured },
                "X", topics, profile, null, policy);
            AssertEqual("detect.structuredPair.topic", "archotech", plan.matchedTopics[0]);

            // Context-PAIR trigger: key AND exact stable value must both match.
            plan = CultureAnnotationPlanner.Plan(
                new List<AnnotationFieldView> { Field(0, "GameContext", "bionic", "part_tier") },
                "X", topics, profile, null, policy);
            AssertEqual("detect.pair.miss", 0, plan.entries.Count);
            plan = CultureAnnotationPlanner.Plan(
                new List<AnnotationFieldView> { Field(0, "GameContext", "archotech", "part_tier") },
                "X", topics, profile, null, policy);
            AssertEqual("detect.pair.hit", "archotech", plan.matchedTopics[0]);

            // Value-MARKER trigger: the stable schema token inside a scannable field's text.
            plan = CultureAnnotationPlanner.Plan(
                new List<AnnotationFieldView> { Field(2, "PawnSummary", "sex=female; xenotype=Hussar") },
                "X", topics, profile, null, policy);
            AssertEqual("detect.marker.topic", "xenohumans", plan.matchedTopics[0]);
            AssertEqual("detect.marker.field", 2, plan.entries[0].fieldIndex);

            // DefName trigger anchors to the FIRST scannable field.
            plan = CultureAnnotationPlanner.Plan(
                new List<AnnotationFieldView>
                {
                    Field(3, "EventNoun", "royal ceremony"),
                    Field(4, "PovText", "knelt before the throne")
                },
                "RoyalTitleGained", topics, profile, null, policy);
            AssertEqual("detect.defName.topic", "empire", plan.matchedTopics[0]);
            AssertEqual("detect.defName.field", 3, plan.entries[0].fieldIndex);
        }

        private static void TestAnnotationCapsAndPriority()
        {
            CultureTopicRule low = Topic("psychic", 10);
            low.triggerValueMarkers.Add("psylink_level=");
            CultureTopicRule mid = Topic("empire", 20);
            mid.triggerValueMarkers.Add("title=");
            CultureTopicRule high = Topic("xenohumans", 30);
            high.triggerValueMarkers.Add("xenotype=");
            List<CultureTopicRule> topics = new List<CultureTopicRule> { high, mid, low };
            CultureProfile profile = Profile("Sophian",
                "psychic", "rank visible", "empire", "true order", "xenohumans", "station body");
            List<AnnotationFieldView> fields = new List<AnnotationFieldView>
            {
                Field(0, "PawnSummary", "xenotype=Hussar; title=Knight; psylink_level=3")
            };
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();

            // At most two DISTINCT topics, ascending order wins the slots.
            CultureAnnotationPlan plan = CultureAnnotationPlanner.Plan(
                fields, "X", topics, profile, null, policy);
            AssertEqual("caps.count", 2, plan.entries.Count);
            AssertEqual("caps.first", "psychic", plan.matchedTopics[0]);
            AssertEqual("caps.second", "empire", plan.matchedTopics[1]);

            // A topic with NO clause never consumes a slot; the next topic takes it.
            CultureProfile sparse = Profile("Sophian", "xenohumans", "station body");
            plan = CultureAnnotationPlanner.Plan(fields, "X", topics, sparse, null, policy);
            AssertEqual("caps.skipClauseless.count", 1, plan.entries.Count);
            AssertEqual("caps.skipClauseless.topic", "xenohumans", plan.matchedTopics[0]);
        }

        private static void TestAnnotationOriginAdoptedRendering()
        {
            CultureTopicRule topic = Topic("void", 10);
            topic.triggerContextKeys.Add("dark_study");
            List<CultureTopicRule> topics = new List<CultureTopicRule> { topic };
            List<AnnotationFieldView> fields = new List<AnnotationFieldView>
            {
                Field(0, "GameContext", "entity research", "dark_study")
            };
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            CultureProfile origin = Profile("Rustican", "void", "burn it out");
            CultureProfile adopted = Profile("Corunan", "void", "hungry spirit");

            // Distinct origin + adopted profiles ⇒ the dual format with both clauses (§4.3).
            CultureAnnotationPlan plan = CultureAnnotationPlanner.Plan(
                fields, "X", topics, origin, adopted, policy);
            AssertEqual("dual.text", "(origin: burn it out; adopted: hungry spirit)",
                plan.entries[0].text);

            // Same culture on both sides ⇒ the single format.
            plan = CultureAnnotationPlanner.Plan(fields, "X", topics, origin,
                Profile("Rustican", "void", "burn it out"), policy);
            AssertEqual("dual.same", "(culture: burn it out)", plan.entries[0].text);

            // One side missing a clause ⇒ the single format with the side that has one.
            plan = CultureAnnotationPlanner.Plan(fields, "X", topics,
                Profile("Rustican"), adopted, policy);
            AssertEqual("dual.oneSide", "(culture: hungry spirit)", plan.entries[0].text);

            // No profile at all ⇒ nothing, no fallback prose (§4.3).
            plan = CultureAnnotationPlanner.Plan(fields, "X", topics, null, null, policy);
            AssertEqual("dual.noProfile", 0, plan.entries.Count);
        }

        private static void TestAnnotationMasterSwitchAndScannableSources()
        {
            CultureTopicRule topic = Topic("xenohumans", 10);
            topic.triggerValueMarkers.Add("xenotype=");
            List<CultureTopicRule> topics = new List<CultureTopicRule> { topic };
            CultureProfile profile = Profile("Rustican", "xenohumans", "tool fixed");
            List<AnnotationFieldView> fields = new List<AnnotationFieldView>
            {
                Field(0, "PawnSummary", "xenotype=Hussar")
            };

            // The one player switch controls injection: off ⇒ no annotations (§3.2).
            KnowledgePolicySnapshot off = KnowledgePolicySnapshot.CreateDefault();
            off.injectionEnabled = false;
            AssertEqual("switch.off", 0,
                CultureAnnotationPlanner.Plan(fields, "X", topics, profile, null, off).entries.Count);

            // Past-memory text and prior entries are structurally unscannable: their sources are
            // simply absent from the allowlist, so markers inside them can never trigger.
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            List<AnnotationFieldView> excluded = new List<AnnotationFieldView>
            {
                Field(0, "MemoryContext", "- (day) xenotype=Hussar changed"),
                Field(1, "EntryText", "yesterday xenotype=Hussar"),
                Field(2, "HiddenInitiatorEntry", "xenotype=Hussar")
            };
            AssertEqual("sources.excluded", 0,
                CultureAnnotationPlanner.Plan(excluded, "X", topics, profile, null, policy).entries.Count);

            // Sentinel-valued fields never trigger either.
            AssertEqual("sources.sentinel", 0,
                CultureAnnotationPlanner.Plan(
                    new List<AnnotationFieldView> { Field(0, "PawnSummary", "none") },
                    "X", topics, profile, null, policy).entries.Count);
        }

        private static void TestAnnotationRecursionPrevention()
        {
            // The planner runs once, pre-annotation; an annotation-shaped string in a scannable
            // field only triggers when it carries a REAL structured marker — the parenthetical
            // format itself is inert.
            CultureTopicRule topic = Topic("void", 10);
            topic.triggerValueMarkers.Add("dark_study=");
            List<CultureTopicRule> topics = new List<CultureTopicRule> { topic };
            CultureProfile profile = Profile("Rustican", "void", "burn it out");
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            CultureAnnotationPlan plan = CultureAnnotationPlanner.Plan(
                new List<AnnotationFieldView>
                {
                    Field(0, "PovText", "he said (culture: burn it out) twice")
                },
                "X", topics, profile, null, policy);
            AssertEqual("recursion.inert", 0, plan.entries.Count);
        }

        // ── Shipped-catalog contracts ────────────────────────────────────────────────────────────────

        private static void TestShippedCatalogContract()
        {
            AssertTrue("catalog.nonEmpty", shippedRules.Count >= 20);
            HashSet<string> defNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ImportantEventRule rule in shippedRules)
            {
                AssertTrue("catalog." + rule.defName + ".defName", rule.defName.Length > 0);
                AssertTrue("catalog." + rule.defName + ".unique", defNames.Add(rule.defName));
                AssertTrue("catalog." + rule.defName + ".kind", rule.eventKind.Length > 0);
                AssertTrue("catalog." + rule.defName + ".template", rule.lineTemplate.Length > 0);
                AssertTrue("catalog." + rule.defName + ".matcher",
                    rule.matchDefNames.Count > 0 || rule.matchSuffixes.Count > 0
                    || rule.requireContext.Count > 0);
                bool provided = string.Equals(rule.owners, KnowledgeTokens.OwnersProvided,
                    StringComparison.OrdinalIgnoreCase);
                bool eventChannel = string.Equals(rule.signal, KnowledgeTokens.SignalEvent,
                    StringComparison.OrdinalIgnoreCase);
                // Non-event channels must use provided owners; event channels must not.
                AssertEqual("catalog." + rule.defName + ".ownersChannel", !eventChannel, provided);
            }

            // The RU DefInjected file must translate every def's label AND lineTemplate — a
            // missing row would silently ship English into Russian prompts.
            string ruPath = Path.Combine(RepoRoot(), "Languages", "Russian (Русский)",
                "DefInjected", "PawnDiary.DiaryImportantEventDef", "DiaryImportantEventDefs.xml");
            XDocument ru = XDocument.Load(ruPath);
            foreach (ImportantEventRule rule in shippedRules)
            {
                XElement label = ru.Root.Element(rule.defName + ".label");
                XElement line = ru.Root.Element(rule.defName + ".lineTemplate");
                AssertTrue("catalog.ru." + rule.defName + ".label",
                    label != null && !string.IsNullOrWhiteSpace(label.Value));
                AssertTrue("catalog.ru." + rule.defName + ".line",
                    line != null && !string.IsNullOrWhiteSpace(line.Value));
            }
        }

        private static void TestShippedCultureContractAndRussianParity()
        {
            string root = RepoRoot();
            XDocument topicsDoc = XDocument.Load(Path.Combine(root, "1.6", "Defs", "DiaryCultureTopicDefs.xml"));
            XDocument profilesDoc = XDocument.Load(Path.Combine(root, "1.6", "Defs", "DiaryCultureProfileDefs.xml"));

            List<string> topicKeys = topicsDoc.Root.Elements("PawnDiary.DiaryCultureTopicDef")
                .Select(def => (string)def.Element("topicKey")).ToList();
            AssertEqual("cultureXml.topicCount", 8, topicKeys.Count);
            AssertEqual("cultureXml.topicUnique", 8,
                new HashSet<string>(topicKeys, StringComparer.OrdinalIgnoreCase).Count);

            List<XElement> profiles = profilesDoc.Root
                .Elements("PawnDiary.DiaryCultureProfileDef").ToList();
            // The four Core cultures + Royalty's Sophian (§4.2).
            string[] expectedCultures = { "Astropolitan", "Corunan", "Rustican", "Kriminul", "Sophian" };
            AssertEqual("cultureXml.profileCount", expectedCultures.Length, profiles.Count);
            int fallbacks = 0;
            foreach (XElement profile in profiles)
            {
                string culture = (string)profile.Element("cultureDefName");
                AssertContains("cultureXml.culture", expectedCultures.ToList(), culture);
                if (string.Equals((string)profile.Element("isFallback"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    fallbacks++;
                }

                List<XElement> clauses = profile.Element("clauses").Elements("li").ToList();
                AssertEqual("cultureXml." + culture + ".clauseCount", 8, clauses.Count);
                foreach (XElement clause in clauses)
                {
                    string topicKey = (string)clause.Element("topicKey");
                    string text = ((string)clause.Element("clause") ?? string.Empty).Trim();
                    AssertContains("cultureXml." + culture + ".topic", topicKeys, topicKey);
                    AssertTrue("cultureXml." + culture + "." + topicKey + ".len80 (" + text.Length + ")",
                        text.Length > 0 && text.Length <= 80);
                }
            }

            AssertEqual("cultureXml.fallbackUnique", 1, fallbacks);

            // Russian parity: every clause row is translated by list index, ≤80 chars.
            string ruPath = Path.Combine(root, "Languages", "Russian (Русский)",
                "DefInjected", "PawnDiary.DiaryCultureProfileDef", "DiaryCultureProfileDefs.xml");
            XDocument ruDoc = XDocument.Load(ruPath);
            foreach (XElement profile in profiles)
            {
                string defName = (string)profile.Element("defName");
                for (int i = 0; i < 8; i++)
                {
                    string tag = defName + ".clauses." + i + ".clause";
                    XElement row = ruDoc.Root.Element(tag);
                    AssertTrue("cultureXml.ru." + tag, row != null);
                    string text = row.Value.Trim();
                    AssertTrue("cultureXml.ru." + tag + ".len80 (" + text.Length + ")",
                        text.Length > 0 && text.Length <= 80);
                }
            }
        }

        // ── Assert helpers ───────────────────────────────────────────────────────────────────────────

        private static void AssertContains(string label, List<string> values, string expected)
        {
            assertions++;
            if (!values.Contains(expected))
            {
                throw new InvalidOperationException(label + ": missing " + expected
                    + " in [" + string.Join(",", values) + "]");
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

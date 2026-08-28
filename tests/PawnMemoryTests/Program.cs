// Standalone, no-RimWorld checks for the deterministic pawn-knowledge system
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §8): important-event classification against the shipped
// XML allowlist (positives, owners, dedup, and explicit negatives), localized line rendering with
// prompt-safe manual overrides and fallbacks, deterministic retrieval (participant/exact-key
// eligibility, tier ranking, stable
// ties, the two-record cap, and proof that broad topic overlap alone can never recall), culture
// resolution (ideology/faction paths, legacy inference, conversion replacement, unknown
// cultures), field-aware topic annotation (structured and localized-text detection, caps,
// origin/adopted, master switch, recursion prevention), defensive-cap eviction planning, and
// shipped-XML contract checks incl. Russian parity. The project links only pure source, so any
// accidental Verse/Unity dependency is a compile-time failure.
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
            TestMalformedPolicyNormalization();
            TestPlayerMemoryPolicy();
            TestSentinelValues();
            TestClassifierPositiveCatalog();
            TestClassifierNegativeCatalog();
            TestClassifierOwnersAndParticipants();
            TestClassifierBirthChildIdentity();
            TestClassifierContextGatesAndSubjects();
            TestIdentityPrefilter();
            TestClassifierDedupDeterminism();
            TestClassifierFirstMatchOrder();
            TestM7FactualCaptureAndExactRouting();
            TestM7FactualRefusalAndAuthoritativeOwnership();
            TestLineRendererTemplatesAndFallback();
            TestComposeBlockCaps();
            TestSelectorEligibilityDoors();
            TestSelectorRequiredParticipantDoor();
            TestSelectorBackgroundFallback();
            TestSelectorRankingAndStableTies();
            TestSelectorTwoRecordCapAndReports();
            TestSelectorSelfEcho();
            TestSelectorExcludedSourceEvents();
            TestSelectorBroadTopicNeverRecalls();
            TestRecallV2ConsumerAndExactEligibilityContract();
            TestRecallV2RepetitionBoundariesAndGuardCompleteness();
            TestRecallV2FrozenRevalidationAndPairedPrivacy();
            TestRecallV2FrozenSelectionSaveCodec();
            TestRecallV2CurrentTruthAndCurrentReleaseComparison();
            TestRecallV2AdversarialIdentityAndCapMatrix();
            TestQueryBuildFromRulesAndPolicy();
            TestSocialReflectionNeverClassifiesAsKnowledge();
            TestEvictionPerPawnCap();
            TestEvictionGlobalCapAbsentFirst();
            TestEvictionDeterminismAndNoMutation();
            TestEvictionProtectedRows();
            TestCultureResolutionPaths();
            TestCultureLegacyInferenceAndStability();
            TestCultureConversionReplacement();
            TestFamilyRelationDirection();
            TestM6ObservationPolicyAndXmlParity();
            TestM6ReconciliationSchedulingAndPublication();
            TestM6VisibilityAndExactIdentity();
            TestM6FactCanonicalization();
            TestM6DuplicateRepairPolicy();
            TestM6SilentBaselinesAndCapacityMarkers();
            TestM6DeterministicOpinionEpisodes();
            TestM6FactionOwnerAwarenessSeparation();
            TestObservationSourceCopySlices();
            TestObservationIndexRefreshArchitecture();
            TestMigrationUsesRunningBudgetProjection();
            TestRoutineAdmissionUsesOwnerLocalCopiesAndIndexes();
            TestDispatchUsesOwnerLocalByteRefresh();
            TestRuntimeMemoryWiringArchitecture();
            TestAnnotationTopicDetectionPerField();
            TestAnnotationLocalizedTextTerms();
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
                    lineTemplate = (string)def.Element("lineTemplate") ?? string.Empty,
                    captureSourceToken = (string)def.Element("captureSourceToken") ?? string.Empty,
                    memoryKind = (string)def.Element("memoryKind") ?? string.Empty,
                    memoryCategory = (string)def.Element("memoryCategory") ?? string.Empty,
                    baseImportance = (string)def.Element("baseImportance") ?? string.Empty,
                    consolidationEligible = ParseBool(def, "consolidationEligible"),
                    authoritativePageOwned = ParseBool(def, "authoritativePageOwned")
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
                rule.promptConsumerIds.AddRange(ListItems(def, "promptConsumerIds"));
                rule.authoritativeRelationDefNames.AddRange(
                    ListItems(def, "authoritativeRelationDefNames"));
                XElement memoryFacts = def.Element("memoryFacts");
                if (memoryFacts != null)
                {
                    foreach (XElement row in memoryFacts.Elements("li"))
                    {
                        MemoryFactDescriptor descriptor = new MemoryFactDescriptor
                        {
                            factKind = (string)row.Element("factKind") ?? string.Empty,
                            contextKey = (string)row.Element("contextKey") ?? string.Empty,
                            aggregationToken = (string)row.Element("aggregationToken") ?? string.Empty,
                            canonicalValueKind = (string)row.Element("canonicalValueKind") ?? string.Empty
                        };
                        descriptor.allowedStates.AddRange(ListItems(row, "allowedStates"));
                        rule.memoryFacts.Add(descriptor);
                    }
                }
                XElement route = def.Element("threadRoute");
                if (route != null)
                {
                    rule.threadRoute = new MemoryThreadRouteRule
                    {
                        subjectKind = (string)route.Element("subjectKind") ?? string.Empty,
                        chapterPhasePolicy = (string)route.Element("chapterPhasePolicy") ?? string.Empty,
                        chapterDirective = (string)route.Element("chapterDirective")
                            ?? MemoryChapterDirectiveTokens.ContinueCurrent,
                        chapterClosureReasonToken =
                            (string)route.Element("chapterClosureReasonToken") ?? string.Empty,
                        fallbackLabelSource =
                            (string)route.Element("fallbackLabelSource") ?? string.Empty
                    };
                    rule.threadRoute.equivalentExtractors.AddRange(
                        ListItems(route, "equivalentExtractors").Select(value =>
                            new MemoryRouteExtractor { extractorToken = value }));
                }
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

        private static bool ParseBool(XElement parent, string name)
        {
            bool parsed;
            return bool.TryParse((string)parent.Element(name), out parsed) && parsed;
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
                sourceOccurrenceId = eventId,
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
            AssertEqual("default.playerAuthoredChars", 450, policy.playerAuthoredMemoryMaxChars);
            AssertEqual("default.lines", 2, policy.relevantPastMaxLines);
            AssertEqual("default.backgroundFormat.safeFallback", "{0}",
                policy.backgroundMemoryLineFormat);
            AssertEqual("default.currentStateInstruction.safeFallback", string.Empty,
                policy.currentStateInstruction);
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
            AssertEqual("xml.playerAuthoredChars", policy.playerAuthoredMemoryMaxChars,
                int.Parse((string)def.Element("playerAuthoredMemoryMaxChars")));
            AssertEqual("xml.topics", policy.maxCultureTopicsPerPrompt,
                int.Parse((string)def.Element("maxCultureTopicsPerPrompt")));
            AssertEqual("xml.lineFormat", policy.relevantPastLineFormat,
                (string)def.Element("relevantPastLineFormat"));
            AssertEqual("xml.singleFormat", policy.annotationSingleFormat,
                (string)def.Element("annotationSingleFormat"));
            AssertEqual("xml.dualFormat", policy.annotationDualFormat,
                (string)def.Element("annotationDualFormat"));
            string backgroundFormat = (string)def.Element("backgroundMemoryLineFormat");
            string currentStateInstruction = (string)def.Element("currentStateInstruction");
            AssertTrue("xml.backgroundFormat.nonblank", !string.IsNullOrWhiteSpace(backgroundFormat));
            AssertTrue("xml.backgroundFormat.placeholder", backgroundFormat.Contains("{0}"));
            AssertTrue("xml.currentStateInstruction.nonblank",
                !string.IsNullOrWhiteSpace(currentStateInstruction));

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

        private static void TestMalformedPolicyNormalization()
        {
            KnowledgePolicySnapshot malformed = KnowledgePolicySnapshot.CreateDefault();
            malformed.maxRecordsPerPawn = 0;
            malformed.maxRecordsGlobal = -1;
            malformed.fallbackSummaryMaxChars = 0;
            malformed.playerAuthoredMemoryMaxChars = -10;
            malformed.relevantPastMaxLines = -2;
            malformed.relevantPastMaxChars = 0;
            malformed.maxCultureTopicsPerPrompt = -3;

            KnowledgePolicySnapshot normalized = KnowledgePolicyNormalization.Normalize(malformed);
            KnowledgePolicySnapshot defaults = KnowledgePolicySnapshot.CreateDefault();
            AssertEqual("normalize.perPawn", defaults.maxRecordsPerPawn, normalized.maxRecordsPerPawn);
            AssertEqual("normalize.global", defaults.maxRecordsGlobal, normalized.maxRecordsGlobal);
            AssertEqual("normalize.fallbackChars",
                defaults.fallbackSummaryMaxChars, normalized.fallbackSummaryMaxChars);
            AssertEqual("normalize.playerAuthoredChars",
                defaults.playerAuthoredMemoryMaxChars, normalized.playerAuthoredMemoryMaxChars);
            AssertEqual("normalize.lines", defaults.relevantPastMaxLines, normalized.relevantPastMaxLines);
            AssertEqual("normalize.chars", defaults.relevantPastMaxChars, normalized.relevantPastMaxChars);
            AssertEqual("normalize.topics",
                defaults.maxCultureTopicsPerPrompt, normalized.maxCultureTopicsPerPrompt);
            AssertEqual("normalize.eviction.zero",
                KnowledgePolicyNormalization.DefaultEvictionScanIntervalTicks,
                KnowledgePolicyNormalization.EvictionScanIntervalTicks(0));
            AssertEqual("normalize.eviction.negative",
                KnowledgePolicyNormalization.DefaultEvictionScanIntervalTicks,
                KnowledgePolicyNormalization.EvictionScanIntervalTicks(-25));
            AssertEqual("normalize.eviction.valid",
                60000, KnowledgePolicyNormalization.EvictionScanIntervalTicks(60000));
        }

        private static void TestPlayerMemoryPolicy()
        {
            AssertEqual("playerMemory.source.blank", KnowledgeTokens.SourceKindCaptured,
                PlayerMemoryPolicy.NormalizeSourceKind(null));
            AssertEqual("playerMemory.source.unknown", KnowledgeTokens.SourceKindCaptured,
                PlayerMemoryPolicy.NormalizeSourceKind("future-source"));
            AssertEqual("playerMemory.source.playerCase", KnowledgeTokens.SourceKindPlayer,
                PlayerMemoryPolicy.NormalizeSourceKind(" PLAYER "));
            AssertEqual("playerMemory.scope.blank", KnowledgeTokens.RecallScopeContextual,
                PlayerMemoryPolicy.NormalizeRecallScope(string.Empty));
            AssertEqual("playerMemory.scope.unknown", KnowledgeTokens.RecallScopeContextual,
                PlayerMemoryPolicy.NormalizeRecallScope("future-scope"));
            AssertEqual("playerMemory.scope.backgroundCase", KnowledgeTokens.RecallScopeBackground,
                PlayerMemoryPolicy.NormalizeRecallScope(" BACKGROUND "));

            const string owner = "Thing_Human42";
            string canonicalId = owner + "|" + KnowledgeTokens.EventKindPlayerBackstory;
            AssertEqual("playerMemory.canonical.record", canonicalId,
                PlayerMemoryPolicy.CanonicalBackstoryRecordId(owner));
            AssertEqual("playerMemory.canonical.dedup", canonicalId,
                PlayerMemoryPolicy.CanonicalBackstoryDedupKey(owner));

            PlayerMemoryMutationPlan missingOwner = PlayerMemoryPolicy.PlanBackstoryMutation(
                "  ", null, "text", 450);
            AssertEqual("playerMemory.missingOwner.action", PlayerMemoryMutationAction.Rejected,
                missingOwner.action);
            AssertEqual("playerMemory.missingOwner.error",
                PlayerMemoryValidationError.MissingOwnerPawnId, missingOwner.error);

            PlayerMemoryMutationPlan tooLong = PlayerMemoryPolicy.PlanBackstoryMutation(
                owner, null, new string('x', 451), 450);
            AssertEqual("playerMemory.tooLong.action", PlayerMemoryMutationAction.Rejected,
                tooLong.action);
            AssertEqual("playerMemory.tooLong.error", PlayerMemoryValidationError.TextTooLong,
                tooLong.error);
            AssertEqual("playerMemory.tooLong.notTruncated", 451, tooLong.normalizedText.Length);
            PlayerMemoryMutationPlan defaultLimit = PlayerMemoryPolicy.PlanBackstoryMutation(
                owner, null, new string('x', 451), 0);
            AssertEqual("playerMemory.defaultLimit", PlayerMemoryValidationError.TextTooLong,
                defaultLimit.error);

            PlayerMemoryMutationPlan create = PlayerMemoryPolicy.PlanBackstoryMutation(
                owner, null, "  <b>lowercase</b>\r\n\t Жизнь   café  ", 450);
            AssertEqual("playerMemory.create.action", PlayerMemoryMutationAction.Create,
                create.action);
            AssertEqual("playerMemory.create.error", PlayerMemoryValidationError.None, create.error);
            AssertEqual("playerMemory.create.normalized", "lowercase Жизнь café",
                create.normalizedText);
            AssertTrue("playerMemory.create.record", create.record != null);
            AssertEqual("playerMemory.create.id", canonicalId, create.record.recordId);
            AssertEqual("playerMemory.create.dedup", canonicalId, create.record.dedupKey);
            AssertEqual("playerMemory.create.owner", owner, create.record.ownerPawnId);
            AssertEqual("playerMemory.create.kind", KnowledgeTokens.EventKindPlayerBackstory,
                create.record.eventKind);
            AssertEqual("playerMemory.create.source", KnowledgeTokens.SourceKindPlayer,
                create.record.sourceKind);
            AssertEqual("playerMemory.create.scope", KnowledgeTokens.RecallScopeBackground,
                create.record.recallScope);
            AssertEqual("playerMemory.create.sourceEvent", string.Empty,
                create.record.sourceEventId);
            AssertEqual("playerMemory.create.date", string.Empty, create.record.dateLabel);
            AssertEqual("playerMemory.create.tick", 0, create.record.tick);
            AssertEqual("playerMemory.create.fallback", string.Empty,
                create.record.fallbackSummary);
            AssertEqual("playerMemory.create.participants", 0, create.record.participants.Count);
            AssertEqual("playerMemory.create.subjects", 0, create.record.subjectKeys.Count);
            AssertEqual("playerMemory.create.facts", 0, create.record.facts.Count);
            AssertTrue("playerMemory.create.canonical",
                PlayerMemoryPolicy.IsCanonicalBackstory(create.record, owner));
            AssertTrue("playerMemory.create.wrongOwner",
                !PlayerMemoryPolicy.IsCanonicalBackstory(create.record, "Thing_Human99"));
            AssertTrue("playerMemory.protected.canonical",
                PlayerMemoryPolicy.IsProtectedFromAutomaticEviction(create.record, owner));
            AssertTrue("playerMemory.protected.canonicalWrongOwner",
                !PlayerMemoryPolicy.IsProtectedFromAutomaticEviction(
                    create.record,
                    "Thing_Human99"));

            ImportantMemoryRecordSnapshot arrivalBoundary = new ImportantMemoryRecordSnapshot
            {
                ownerPawnId = owner,
                recordId = owner + "|arrival-boundary",
                dedupKey = owner + "|arrival-boundary",
                eventKind = KnowledgeTokens.EventKindFactionJoined,
                sourceKind = KnowledgeTokens.SourceKindCaptured,
                recallScope = KnowledgeTokens.RecallScopeContextual
            };
            AssertTrue("playerMemory.protected.arrival",
                PlayerMemoryPolicy.IsProtectedFromAutomaticEviction(arrivalBoundary, owner));
            AssertTrue("playerMemory.protected.arrivalWrongOwner",
                !PlayerMemoryPolicy.IsProtectedFromAutomaticEviction(
                    arrivalBoundary,
                    "Thing_Human99"));
            AssertTrue("playerMemory.protected.arrivalLegacyDefaults",
                PlayerMemoryPolicy.IsProtectedFromAutomaticEviction(
                    owner,
                    "legacy-arrival",
                    "legacy-arrival",
                    KnowledgeTokens.EventKindFactionJoined,
                    null,
                    null));
            AssertTrue("playerMemory.protected.arrivalPlayerRejected",
                !PlayerMemoryPolicy.IsProtectedFromAutomaticEviction(
                    owner,
                    "player-arrival",
                    "player-arrival",
                    KnowledgeTokens.EventKindFactionJoined,
                    KnowledgeTokens.SourceKindPlayer,
                    KnowledgeTokens.RecallScopeContextual));
            AssertTrue("playerMemory.protected.arrivalBackgroundRejected",
                !PlayerMemoryPolicy.IsProtectedFromAutomaticEviction(
                    owner,
                    "background-arrival",
                    "background-arrival",
                    KnowledgeTokens.EventKindFactionJoined,
                    KnowledgeTokens.SourceKindCaptured,
                    KnowledgeTokens.RecallScopeBackground));
            AssertTrue("playerMemory.protected.ordinaryCapturedRejected",
                !PlayerMemoryPolicy.IsProtectedFromAutomaticEviction(
                    owner,
                    "ordinary",
                    "ordinary",
                    "relation.spouse.gained",
                    KnowledgeTokens.SourceKindCaptured,
                    KnowledgeTokens.RecallScopeContextual));

            PlayerMemoryMutationPlan unchanged = PlayerMemoryPolicy.PlanBackstoryMutation(
                owner, create.record, create.normalizedText, 450);
            AssertEqual("playerMemory.unchanged", PlayerMemoryMutationAction.None, unchanged.action);

            PlayerMemoryMutationPlan update = PlayerMemoryPolicy.PlanBackstoryMutation(
                owner, create.record, "changed Сюжет", 450);
            AssertEqual("playerMemory.update.action", PlayerMemoryMutationAction.Update,
                update.action);
            AssertEqual("playerMemory.update.text", "changed Сюжет",
                update.record.manualTextOverride);
            AssertEqual("playerMemory.update.doesNotMutateInput", "lowercase Жизнь café",
                create.record.manualTextOverride);

            PlayerMemoryMutationPlan delete = PlayerMemoryPolicy.PlanBackstoryMutation(
                owner, create.record, " <b> </b>\r\n ", 450);
            AssertEqual("playerMemory.delete", PlayerMemoryMutationAction.Delete, delete.action);
            AssertTrue("playerMemory.delete.noRecord", delete.record == null);
            PlayerMemoryMutationPlan blankAbsent = PlayerMemoryPolicy.PlanBackstoryMutation(
                owner, null, "  ", 450);
            AssertEqual("playerMemory.blankAbsent", PlayerMemoryMutationAction.None,
                blankAbsent.action);

            ImportantMemoryRecordSnapshot noncanonical = new ImportantMemoryRecordSnapshot
            {
                ownerPawnId = owner,
                recordId = canonicalId,
                dedupKey = canonicalId,
                eventKind = KnowledgeTokens.EventKindPlayerBackstory,
                sourceKind = KnowledgeTokens.SourceKindCaptured,
                recallScope = KnowledgeTokens.RecallScopeBackground,
                manualTextOverride = "must not be edited"
            };
            AssertTrue("playerMemory.noncanonical.rejected",
                !PlayerMemoryPolicy.IsCanonicalBackstory(noncanonical, owner));
            PlayerMemoryMutationPlan safeCreate = PlayerMemoryPolicy.PlanBackstoryMutation(
                owner, noncanonical, "new canon", 450);
            AssertEqual("playerMemory.noncanonical.treatedAbsent", PlayerMemoryMutationAction.Create,
                safeCreate.action);
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

        private static void TestM7FactualCaptureAndExactRouting()
        {
            List<ImportantMemoryDraft> paired = Classify(EventSignal(
                "Lover", string.Empty, "Pawn_A", "Pawn_B", "page-relationship-1", 4200));
            AssertEqual("m7.event.paired-count", 2, paired.Count);
            AssertTrue("m7.event.factual-present", paired.All(row => row.factual != null));
            AssertTrue("m7.event.same-occurrence", paired.All(row =>
                row.factual.sourceOccurrenceId == "page-relationship-1"
                && row.factual.sourceEventId == "page-relationship-1"
                && row.factual.sourceKindToken == "diary_event"));
            AssertEqual("m7.event.owner-a-subject", "Pawn_B",
                paired.First(row => row.ownerPawnId == "Pawn_A").factual.subjectId);
            AssertEqual("m7.event.owner-b-subject", "Pawn_A",
                paired.First(row => row.ownerPawnId == "Pawn_B").factual.subjectId);
            AssertTrue("m7.event.private-fact-identities", paired[0].factual.facts[0].factId
                != paired[1].factual.facts[0].factId);
            AssertEqual("m7.event.category", MemoryContractTokens.CategoryRelationships,
                paired[0].factual.category);
            AssertEqual("m7.event.chapter-directive",
                MemoryChapterDirectiveTokens.CloseAndStartWithCurrentEvent,
                paired[0].factual.chapterDirective);
            AssertEqual("m7.event.chapter-phase", "relationship_phase",
                paired[0].factual.chapterPhaseToken);
            string ownerARecord;
            string ownerBRecord;
            AssertTrue("m7.event.private-record-a", MemoryIdentityCodec.TryCreateRecordId(
                new MemoryRecordIdentity
                {
                    ownerPawnId = paired[0].factual.ownerPawnId,
                    ownerEpochToken = M6Epoch(paired[0].factual.ownerPawnId),
                    sourceOccurrenceId = paired[0].factual.sourceOccurrenceId,
                    captureRuleId = paired[0].factual.captureRuleId,
                    factDiscriminator = paired[0].factual.factDiscriminator
                }, out ownerARecord));
            AssertTrue("m7.event.private-record-b", MemoryIdentityCodec.TryCreateRecordId(
                new MemoryRecordIdentity
                {
                    ownerPawnId = paired[1].factual.ownerPawnId,
                    ownerEpochToken = M6Epoch(paired[1].factual.ownerPawnId),
                    sourceOccurrenceId = paired[1].factual.sourceOccurrenceId,
                    captureRuleId = paired[1].factual.captureRuleId,
                    factDiscriminator = paired[1].factual.factDiscriminator
                }, out ownerBRecord));
            AssertTrue("m7.event.paired-pov-private-dedup", ownerARecord != ownerBRecord);

            KnowledgeCaptureSignal observation = new KnowledgeCaptureSignal
            {
                signal = KnowledgeTokens.SignalMemoryOpinionEpisode,
                defName = "memory.opinion.episode",
                providedOwnerPawnId = "Pawn_A",
                tick = 4300,
                sourceLocalSequenceInvariant = 7,
                sourceProvesUniqueness = true,
                gameContext = "subject_pawn_id=Pawn_B; subject_name=Brik; "
                    + "episode_value=reason:band_crossing|from:0|to:30|from_band:neutral|to_band:friendly; "
                    + "from_opinion_band=neutral; to_opinion_band=friendly"
            };
            ImportantMemoryDraft first = Classify(observation)[0];
            ImportantMemoryDraft repeated = Classify(observation)[0];
            AssertTrue("m7.fallback.factual", first.factual != null);
            AssertEqual("m7.fallback.deterministic", first.factual.sourceOccurrenceId,
                repeated.factual.sourceOccurrenceId);
            AssertEqual("m7.fallback.no-page-link", string.Empty,
                first.factual.sourceEventId);
            AssertEqual("m7.fallback.capture-signal", "capture_signal",
                first.factual.sourceKindToken);
            AssertEqual("m7.fallback.exact-route", "Pawn_B", first.factual.subjectId);
            AssertEqual("m7.fallback.chapter-phase", "opinion_episode",
                first.factual.chapterPhaseToken);
            AssertTrue("m7.fallback.factual-wording", first.factual.automaticWording.Contains("Brik"));
            AssertTrue("m7.fallback.milestone-wording",
                first.factual.automaticWording.Contains("neutral")
                    && first.factual.automaticWording.Contains("friendly"));
        }

        private static void TestM7FactualRefusalAndAuthoritativeOwnership()
        {
            ImportantEventRule ambiguous = M7TestRule();
            ambiguous.threadRoute.equivalentExtractors.Add(
                new MemoryRouteExtractor { extractorToken = "context:alternate_id" });
            KnowledgeCaptureSignal signal = new KnowledgeCaptureSignal
            {
                signal = "m7Test",
                defName = "m7.test",
                providedOwnerPawnId = "Pawn_A",
                sourceOccurrenceId = "occurrence-1",
                tick = 5000,
                gameContext = "subject_id=Pawn_B; alternate_id=Pawn_C"
            };
            ImportantMemoryDraft ambiguousDraft = ImportantEventClassifier.Classify(
                signal, new List<ImportantEventRule> { ambiguous },
                KnowledgePolicySnapshot.CreateDefault())[0];
            AssertTrue("m7.ambiguous.factual-still-standalone",
                ambiguousDraft.factual != null && !ambiguousDraft.factual.routeReliable);
            AssertEqual("m7.ambiguous.never-guesses-secondary", "Pawn_A",
                ambiguousDraft.factual.primarySubject.subjectId);

            ImportantEventRule ownerSelf = M7TestRule();
            signal.gameContext = "subject_id=Pawn_A";
            ImportantMemoryDraft selfDraft = ImportantEventClassifier.Classify(
                signal, new List<ImportantEventRule> { ownerSelf },
                KnowledgePolicySnapshot.CreateDefault())[0];
            AssertTrue("m7.owner-self.standalone",
                selfDraft.factual != null && !selfDraft.factual.routeReliable);
            AssertEqual("m7.owner-self.subject-owner", "Pawn_A",
                selfDraft.factual.primarySubject.subjectId);

            ImportantEventRule invalidValue = M7TestRule();
            invalidValue.memoryFacts[0].contextKey = "required_value";
            invalidValue.memoryFacts[0].aggregationToken = MemoryFactContractTokens.OrdinalSet;
            invalidValue.memoryFacts[0].canonicalValueKind = MemoryFactContractTokens.ValueOrdinal;
            signal.gameContext = "subject_id=Pawn_B";
            ImportantMemoryDraft refused = ImportantEventClassifier.Classify(
                signal, new List<ImportantEventRule> { invalidValue },
                KnowledgePolicySnapshot.CreateDefault())[0];
            AssertTrue("m7.invalid-value.legacy-survives", refused.record != null);
            AssertTrue("m7.invalid-value.factual-refused", refused.factual == null);

            AssertTrue("m7.page-ownership.romance",
                ImportantEventClassifier.AuthoritativePageOwnsRelationTransition(
                    new[] { "Lover" }, new[] { "ExLover" }, shippedRules));
            AssertTrue("m7.page-ownership.non-page-social",
                !ImportantEventClassifier.AuthoritativePageOwnsRelationTransition(
                    new[] { "Friend" }, new[] { "Rival" }, shippedRules));

            ImportantEventRule duplicateFact = M7TestRule();
            duplicateFact.memoryFacts.Add(new MemoryFactDescriptor
            {
                factKind = "m7.fact",
                aggregationToken = MemoryFactContractTokens.CountOccurrences,
                canonicalValueKind = MemoryFactContractTokens.ValueEmpty
            });
            AssertEqual("m7.one-category-owner.duplicate-refused",
                "memory_contract_duplicate_fact_category",
                MemoryThreadRoutingPolicy.ValidateRuleContract(duplicateFact));
        }

        private static ImportantEventRule M7TestRule()
        {
            ImportantEventRule rule = new ImportantEventRule
            {
                defName = "M7_TestRule",
                eventKind = "m7.test",
                signal = "m7Test",
                owners = KnowledgeTokens.OwnersProvided,
                captureSourceToken = "test",
                memoryKind = MemoryContractTokens.KindEvent,
                memoryCategory = MemoryContractTokens.CategoryRelationships,
                baseImportance = MemoryContractTokens.ImportanceRegular,
                lineTemplate = "test"
            };
            rule.matchDefNames.Add("m7.test");
            rule.memoryFacts.Add(new MemoryFactDescriptor
            {
                factKind = "m7.fact",
                aggregationToken = MemoryFactContractTokens.CountOccurrences,
                canonicalValueKind = MemoryFactContractTokens.ValueEmpty
            });
            rule.threadRoute = new MemoryThreadRouteRule
            {
                subjectKind = MemoryContractTokens.SubjectPawn,
                chapterPhasePolicy = "test_phase",
                fallbackLabelSource = "context:subject_name"
            };
            rule.threadRoute.equivalentExtractors.Add(
                new MemoryRouteExtractor { extractorToken = "context:subject_id" });
            rule.promptConsumerIds.Add(MemoryRecallConsumerRegistry.OrdinaryDiary);
            return rule;
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

            record.manualTextOverride = "developer-authored memory";
            AssertEqual("render.manualOverride.precedence", "developer-authored memory",
                ImportantMemoryLineRenderer.Render(record, "married {other}", 240));

            record.manualTextOverride = " \r\n\t ";
            AssertEqual("render.manualOverride.blankFallsBack", "married Brik",
                ImportantMemoryLineRenderer.Render(record, "married {other}", 240));

            AssertEqual("render.manualOverride.sanitized", "kept as memory",
                ImportantMemoryLineRenderer.CleanManualOverride(
                    "  <b>kept</b>\r\n\t as   <color=red>memory</color>  ", 240));
            AssertEqual("render.manualOverride.cap", "12345678",
                ImportantMemoryLineRenderer.CleanManualOverride("123456789tail", 8));
            AssertEqual("render.background.localizedFrame",
                "Factual background: lowercase Жизнь café",
                ImportantMemoryLineRenderer.FormatBackground(
                    "lowercase Жизнь café",
                    "Factual background: {0}"));
            AssertEqual("render.background.blankFormat", "lowercase Жизнь café",
                ImportantMemoryLineRenderer.FormatBackground("lowercase Жизнь café", "  "));
            AssertEqual("render.background.malformedFormat", "lowercase Жизнь café",
                ImportantMemoryLineRenderer.FormatBackground("lowercase Жизнь café", "{1}"));
            AssertEqual("render.background.missingPlaceholder", "lowercase Жизнь café",
                ImportantMemoryLineRenderer.FormatBackground(
                    "lowercase Жизнь café",
                    "Factual background"));
            AssertEqual("render.background.escapedPlaceholder", "lowercase Жизнь café",
                ImportantMemoryLineRenderer.FormatBackground("lowercase Жизнь café", "{{0}}"));
            AssertEqual("render.background.blankFact", string.Empty,
                ImportantMemoryLineRenderer.FormatBackground("  ", "Factual background: {0}"));

            string manualSurrogateSafe = ImportantMemoryLineRenderer.CleanManualOverride(
                "1234567\U0001F600tail", 8);
            AssertEqual("render.manualOverride.surrogateSafeCap", "1234567", manualSurrogateSafe);
            AssertTrue("render.manualOverride.surrogateSafeCap.wellFormed",
                manualSurrogateSafe.Length == 0
                || !char.IsHighSurrogate(manualSurrogateSafe[manualSurrogateSafe.Length - 1]));

            record.manualTextOverride = string.Empty;
            AssertEqual("render.cap", "married",
                ImportantMemoryLineRenderer.Render(record, "married {other}", 8));
            record.fallbackSummary = "1234567\U0001F600tail";
            string surrogateSafe = ImportantMemoryLineRenderer.Render(record, "  ", 8);
            AssertEqual("render.surrogateSafeCap", "1234567", surrogateSafe);
            AssertTrue("render.surrogateSafeCap.wellFormed",
                surrogateSafe.Length == 0 || !char.IsHighSurrogate(surrogateSafe[surrogateSafe.Length - 1]));
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

        private static void TestSelectorRequiredParticipantDoor()
        {
            List<ImportantMemoryRecordSnapshot> records = new List<ImportantMemoryRecordSnapshot>
            {
                Record("participant", 100, participantId: "p2"),
                Record("subjectKeyOnly", 300, subjectKey: "pawn:P2"),
                Record("wrongParticipant", 200, participantId: "P9", subjectKey: "pawn:P2")
            };

            // Ordinary retrieval keeps its established participant-OR-exact-subject behavior.
            KnowledgeQuery ordinary = Query(subjectKey: "pawn:P2");
            KnowledgeSelectionResult result = ImportantMemorySelector.Select(
                ordinary, records, KnowledgePolicySnapshot.CreateDefault());
            AssertEqual("requiredParticipant.ordinarySubjectCount", 2, result.selected.Count);

            // H7 retrieval is stricter: only a record that actually names the reflected-on pawn is
            // eligible. Matching remains case-insensitive, like every other stable pawn-ID seam.
            KnowledgeQuery subjectOnly = Query(participantId: "P2", subjectKey: "pawn:P2");
            subjectOnly.requireParticipantOverlap = true;
            result = ImportantMemorySelector.Select(
                subjectOnly, records, KnowledgePolicySnapshot.CreateDefault());
            AssertEqual("requiredParticipant.strictCount", 1, result.selected.Count);
            AssertEqual("requiredParticipant.strictPick", "participant", result.selected[0].recordId);
            AssertEqual(
                "requiredParticipant.subjectKeyRejected",
                KnowledgeRejectReasons.NoOverlap,
                result.report.First(row => row.recordId == "subjectKeyOnly").rejectReason);
            AssertEqual(
                "requiredParticipant.wrongPawnRejected",
                KnowledgeRejectReasons.NoOverlap,
                result.report.First(row => row.recordId == "wrongParticipant").rejectReason);
        }

        private static void TestSelectorBackgroundFallback()
        {
            ImportantMemoryRecordSnapshot background = PlayerMemoryPolicy.PlanBackstoryMutation(
                "P1", null, "I grew up tending mountain goats.", 450).record;
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();

            KnowledgeSelectionResult result = ImportantMemorySelector.Select(
                Query(),
                new List<ImportantMemoryRecordSnapshot> { background },
                policy);
            AssertEqual("backgroundFallback.alone.count", 1, result.selected.Count);
            AssertEqual("backgroundFallback.alone.pick", background.recordId,
                result.selected[0].recordId);

            ImportantMemoryRecordSnapshot contextual =
                Record("contextual", 100, participantId: "P2");
            result = ImportantMemorySelector.Select(
                Query(participantId: "P2"),
                new List<ImportantMemoryRecordSnapshot> { background, contextual },
                policy);
            AssertEqual("backgroundFallback.spareSlot.count", 2, result.selected.Count);
            AssertEqual("backgroundFallback.spareSlot.contextualFirst", "contextual",
                result.selected[0].recordId);
            AssertEqual("backgroundFallback.spareSlot.backgroundSecond", background.recordId,
                result.selected[1].recordId);

            ImportantMemoryRecordSnapshot newer =
                Record("newerContextual", 200, participantId: "P2");
            result = ImportantMemorySelector.Select(
                Query(participantId: "P2"),
                new List<ImportantMemoryRecordSnapshot> { background, contextual, newer },
                policy);
            AssertEqual("backgroundFallback.fullContextual.count", 2, result.selected.Count);
            AssertEqual("backgroundFallback.fullContextual.first", "newerContextual",
                result.selected[0].recordId);
            AssertEqual("backgroundFallback.fullContextual.second", "contextual",
                result.selected[1].recordId);
            AssertEqual("backgroundFallback.fullContextual.report", KnowledgeRejectReasons.OverCap,
                result.report.First(row => row.recordId == background.recordId).rejectReason);

            KnowledgeQuery socialReflection = Query(participantId: "P2");
            socialReflection.requireParticipantOverlap = true;
            result = ImportantMemorySelector.Select(
                socialReflection,
                new List<ImportantMemoryRecordSnapshot> { background, contextual },
                policy);
            AssertEqual("backgroundFallback.participantGate.count", 1, result.selected.Count);
            AssertEqual("backgroundFallback.participantGate.pick", "contextual",
                result.selected[0].recordId);
            AssertEqual("backgroundFallback.participantGate.backgroundRejected",
                KnowledgeRejectReasons.NoOverlap,
                result.report.First(row => row.recordId == background.recordId).rejectReason);

            ImportantMemoryRecordSnapshot wrongOwner = PlayerMemoryPolicy.PlanBackstoryMutation(
                "P9", null, "Other pawn canon.", 450).record;
            ImportantMemoryRecordSnapshot fabricatedPlayerContext =
                Record("fabricatedPlayerContext", 999, participantId: "P2");
            fabricatedPlayerContext.sourceKind = KnowledgeTokens.SourceKindPlayer;
            fabricatedPlayerContext.recallScope = KnowledgeTokens.RecallScopeContextual;
            ImportantMemoryRecordSnapshot capturedBackgroundLookalike =
                Record("capturedBackgroundLookalike", 998, participantId: "P2");
            capturedBackgroundLookalike.recallScope = KnowledgeTokens.RecallScopeBackground;
            result = ImportantMemorySelector.Select(
                Query(participantId: "P2"),
                new List<ImportantMemoryRecordSnapshot>
                {
                    wrongOwner,
                    fabricatedPlayerContext,
                    capturedBackgroundLookalike
                },
                policy);
            AssertEqual("backgroundFallback.lookalikesBlocked", 0, result.selected.Count);
            AssertEqual("backgroundFallback.wrongOwnerRejected", KnowledgeRejectReasons.NoOverlap,
                result.report.First(row => row.recordId == wrongOwner.recordId).rejectReason);
            AssertEqual("backgroundFallback.playerContextRejected", KnowledgeRejectReasons.NoOverlap,
                result.report.First(row => row.recordId == fabricatedPlayerContext.recordId).rejectReason);
            AssertEqual("backgroundFallback.capturedBackgroundRejected", KnowledgeRejectReasons.NoOverlap,
                result.report.First(row => row.recordId == capturedBackgroundLookalike.recordId).rejectReason);
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

        private static void TestSelectorExcludedSourceEvents()
        {
            List<ImportantMemoryRecordSnapshot> records = new List<ImportantMemoryRecordSnapshot>
            {
                Record("canonicalSource", 300, participantId: "P2", sourceEventId: "Source-42"),
                Record("currentReflection", 200, participantId: "P2", sourceEventId: "h7-event"),
                Record("olderSubjectMemory", 100, participantId: "P2", sourceEventId: "older-event")
            };
            KnowledgeQuery query = Query(participantId: "P2", eventId: "H7-EVENT");
            query.requireParticipantOverlap = true;
            query.excludedSourceEventIds.Add(" source-42 ");

            KnowledgeSelectionResult result = ImportantMemorySelector.Select(
                query, records, KnowledgePolicySnapshot.CreateDefault());
            AssertEqual("excludedSource.count", 1, result.selected.Count);
            AssertEqual("excludedSource.pick", "olderSubjectMemory", result.selected[0].recordId);
            AssertEqual(
                "excludedSource.reason",
                KnowledgeRejectReasons.ExcludedSource,
                result.report.First(row => row.recordId == "canonicalSource").rejectReason);
            AssertEqual(
                "excludedSource.selfEchoStillWins",
                KnowledgeRejectReasons.SelfEcho,
                result.report.First(row => row.recordId == "currentReflection").rejectReason);
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

        private static void TestSocialReflectionNeverClassifiesAsKnowledge()
        {
            // H7 pages are important in the diary UI but deliberately absent from the durable-memory
            // allowlist. Otherwise one reflection could become evidence for the next reflection.
            List<ImportantMemoryDraft> drafts = Classify(EventSignal(
                "PawnDiary_SocialReflection",
                "social_reflection=true; social_reflection_subject_id=P2; "
                    + "social_reflection_source_event_id=source-42; relation=Spouse",
                "P1"));
            AssertEqual("socialReflection.notKnowledge", 0, drafts.Count);
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

        private static void TestEvictionProtectedRows()
        {
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            policy.maxRecordsPerPawn = 2;
            policy.maxRecordsGlobal = 20;
            KnowledgeOwnerLoad perPawn = Owner("protected", false, 0, 100, 200);
            perPawn.records[0].protectedFromAutomaticEviction = true;
            KnowledgeEvictionPlan plan = KnowledgeEvictionPlanner.Plan(
                new List<KnowledgeOwnerLoad> { perPawn }, policy);
            AssertEqual("evict.protected.perPawn.count", 1, plan.dropRecordIds.Count);
            AssertEqual("evict.protected.perPawn.oldestCaptured", "protected-1",
                plan.dropRecordIds[0]);
            AssertTrue("evict.protected.perPawn.kept",
                !plan.dropRecordIds.Contains("protected-0"));

            policy.maxRecordsPerPawn = 10;
            policy.maxRecordsGlobal = 1;
            KnowledgeOwnerLoad global = Owner("globalProtected", true, 0, 500);
            global.records[0].protectedFromAutomaticEviction = true;
            plan = KnowledgeEvictionPlanner.Plan(
                new List<KnowledgeOwnerLoad> { global }, policy);
            AssertEqual("evict.protected.global.count", 1, plan.dropRecordIds.Count);
            AssertEqual("evict.protected.global.captured", "globalProtected-1",
                plan.dropRecordIds[0]);
            AssertTrue("evict.protected.global.warn", plan.globalCapHit);

            // A corrupted store with only protected candidates can remain above a defensive cap,
            // but the bounded planner must neither loop nor invent a deletion target.
            policy.maxRecordsPerPawn = 0;
            policy.maxRecordsGlobal = 0;
            KnowledgeOwnerLoad allProtected = Owner("allProtected", false, 0, 1);
            allProtected.records[0].protectedFromAutomaticEviction = true;
            allProtected.records[1].protectedFromAutomaticEviction = true;
            plan = KnowledgeEvictionPlanner.Plan(
                new List<KnowledgeOwnerLoad> { allProtected }, policy);
            AssertEqual("evict.protected.all.count", 0, plan.dropRecordIds.Count);
            AssertTrue("evict.protected.all.noFalseWarning", !plan.globalCapHit);
            AssertEqual("evict.protected.all.noMutation", 2, allProtected.records.Count);
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

        private static void TestM6ObservationPolicyAndXmlParity()
        {
            KnowledgeObservationPolicySnapshot policy =
                new KnowledgeObservationPolicySnapshot().Normalized();
            XElement def = XDocument.Load(Path.Combine(
                RepoRoot(), "1.6", "Defs", "DiaryKnowledgeTuningDef.xml"))
                .Root.Element("PawnDiary.DiaryKnowledgeTuningDef");
            AssertEqual("m6.xml.reconcile", policy.reconciliationIntervalTicks,
                int.Parse((string)def.Element("memoryObservationReconciliationIntervalTicks")));
            AssertEqual("m6.xml.sustain", policy.opinionBandSustainTicks,
                int.Parse((string)def.Element("memoryOpinionBandSustainTicks")));
            AssertEqual("m6.xml.hysteresis", policy.opinionHysteresisPoints,
                int.Parse((string)def.Element("memoryOpinionHysteresisPoints")));
            AssertEqual("m6.xml.inactivity", policy.opinionEpisodeInactivityTicks,
                int.Parse((string)def.Element("memoryOpinionEpisodeInactivityTicks")));
            AssertEqual("m6.xml.maximum", policy.opinionEpisodeMaximumTicks,
                int.Parse((string)def.Element("memoryOpinionEpisodeMaximumTicks")));
            List<string> xmlFamily = def.Element("memoryFamilyRelationDefNames")
                .Elements("li").Select(row => ((string)row).Trim())
                .OrderBy(value => value, StringComparer.Ordinal).ToList();
            AssertEqual("m6.xml.family.count", policy.familyRelationDefNames.Count, xmlFamily.Count);
            AssertEqual("m6.xml.family.exact", string.Join("|", policy.familyRelationDefNames),
                string.Join("|", xmlFamily));

            KnowledgeObservationPolicySnapshot malformed =
                new KnowledgeObservationPolicySnapshot
                {
                    reconciliationIntervalTicks = 0,
                    opinionBandSustainTicks = -1,
                    opinionHysteresisPoints = 100,
                    opinionEpisodeInactivityTicks = 0,
                    opinionEpisodeMaximumTicks = int.MaxValue,
                    maximumStateFacts = 100,
                    maximumFactKeyCharacters = 0,
                    maximumFactValueCharacters = 1000,
                    familyRelationDefNames = new List<string> { "", "Unsafe|Relation" }
                };
            KnowledgeObservationPolicySnapshot normalized = malformed.Normalized();
            AssertEqual("m6.normalize.reconcile", 2500, normalized.reconciliationIntervalTicks);
            AssertEqual("m6.normalize.sustain", 15000, normalized.opinionBandSustainTicks);
            AssertEqual("m6.normalize.hysteresis", 5, normalized.opinionHysteresisPoints);
            AssertEqual("m6.normalize.factCount", 4, normalized.maximumStateFacts);
            AssertEqual("m6.normalize.factKey", 48, normalized.maximumFactKeyCharacters);
            AssertEqual("m6.normalize.factValue", 128, normalized.maximumFactValueCharacters);
            AssertEqual("m6.normalize.familyFallback", 4, normalized.familyRelationDefNames.Count);

            KnowledgeOpinionBandThresholds bands = new KnowledgeOpinionBandThresholds();
            AssertEqual("m6.band.devoted", KnowledgeObservationTokens.OpinionDevoted,
                KnowledgeRelationPolicy.OpinionBandToken(60, bands));
            AssertEqual("m6.band.friendly.high", KnowledgeObservationTokens.OpinionFriendly,
                KnowledgeRelationPolicy.OpinionBandToken(59, bands));
            AssertEqual("m6.band.friendly.low", KnowledgeObservationTokens.OpinionFriendly,
                KnowledgeRelationPolicy.OpinionBandToken(25, bands));
            AssertEqual("m6.band.neutral.high", KnowledgeObservationTokens.OpinionNeutral,
                KnowledgeRelationPolicy.OpinionBandToken(24, bands));
            AssertEqual("m6.band.neutral.low", KnowledgeObservationTokens.OpinionNeutral,
                KnowledgeRelationPolicy.OpinionBandToken(-9, bands));
            AssertEqual("m6.band.strained.high", KnowledgeObservationTokens.OpinionStrained,
                KnowledgeRelationPolicy.OpinionBandToken(-10, bands));
            AssertEqual("m6.band.strained.low", KnowledgeObservationTokens.OpinionStrained,
                KnowledgeRelationPolicy.OpinionBandToken(-39, bands));
            AssertEqual("m6.band.hostile", KnowledgeObservationTokens.OpinionHostile,
                KnowledgeRelationPolicy.OpinionBandToken(-40, bands));
        }

        private static void TestM6ReconciliationSchedulingAndPublication()
        {
            KnowledgeReconciliationSchedulePlan idle =
                KnowledgeRelationPolicy.PlanReconciliationSchedule(
                    1000, 900, false, false, 2500);
            AssertTrue("m6.schedule.idleNoRequest", !idle.requestFullReconciliation);
            KnowledgeReconciliationSchedulePlan due =
                KnowledgeRelationPolicy.PlanReconciliationSchedule(
                    3400, 900, false, false, 2500);
            AssertTrue("m6.schedule.elapsedRequest", due.requestFullReconciliation);
            AssertTrue("m6.schedule.elapsedNotSilent", !due.forceSilentBaseline);

            KnowledgeReconciliationSchedulePlan first =
                KnowledgeRelationPolicy.PlanReconciliationSchedule(
                    100, -1, false, false, 2500);
            AssertTrue("m6.schedule.firstRequest", first.requestFullReconciliation);
            AssertTrue("m6.schedule.firstSilent", first.forceSilentBaseline);

            KnowledgeReconciliationSchedulePlan rollback =
                KnowledgeRelationPolicy.PlanReconciliationSchedule(
                    100, 1000, false, false, 2500);
            AssertTrue("m6.schedule.rollbackConsumesTick", rollback.consumeCompletedTick);
            AssertTrue("m6.schedule.rollbackRequests", rollback.requestFullReconciliation);
            AssertTrue("m6.schedule.rollbackSilent", rollback.forceSilentBaseline);
            KnowledgeReconciliationSchedulePlan rollbackRunning =
                KnowledgeRelationPolicy.PlanReconciliationSchedule(
                    101, 1000, true, false, 2500);
            AssertTrue("m6.schedule.rollbackRunningConsumesTick",
                rollbackRunning.consumeCompletedTick);
            AssertTrue("m6.schedule.rollbackRunningNoRestart",
                !rollbackRunning.requestFullReconciliation);
            KnowledgeReconciliationSchedulePlan rollbackFinishing =
                KnowledgeRelationPolicy.PlanReconciliationSchedule(
                    101, 1000, false, true, 2500);
            AssertTrue("m6.schedule.rollbackFinishingNoRestart",
                !rollbackFinishing.requestFullReconciliation);
            KnowledgeReconciliationSchedulePlan consumed =
                KnowledgeRelationPolicy.PlanReconciliationSchedule(
                    102, -1, true, false, 2500);
            AssertTrue("m6.schedule.consumedDoesNotRestart",
                !consumed.requestFullReconciliation);

            AssertTrue("m6.publication.completeBatch",
                KnowledgeRelationPolicy.ShouldPublishCompletedObservationBatch(
                    true, false, false, false));
            AssertTrue("m6.publication.cleanNoop",
                !KnowledgeRelationPolicy.ShouldPublishCompletedObservationBatch(
                    false, false, false, false));
            AssertTrue("m6.publication.queueDefers",
                !KnowledgeRelationPolicy.ShouldPublishCompletedObservationBatch(
                    true, true, false, false));
            AssertTrue("m6.publication.scanDefers",
                !KnowledgeRelationPolicy.ShouldPublishCompletedObservationBatch(
                    true, false, true, false));
            AssertTrue("m6.publication.finishDefers",
                !KnowledgeRelationPolicy.ShouldPublishCompletedObservationBatch(
                    true, false, false, true));

            KnowledgeObservationWorkMergePlan exactOverridesBaseline =
                KnowledgeRelationPolicy.MergeObservationWorkFlags(
                    false, true, true, false);
            AssertTrue("m6.dirtyMerge.removalSticky",
                exactOverridesBaseline.removedFaction);
            AssertTrue("m6.dirtyMerge.exactNotHidden",
                !exactOverridesBaseline.forceSilentBaseline);
            KnowledgeObservationWorkMergePlan baselinePair =
                KnowledgeRelationPolicy.MergeObservationWorkFlags(
                    false, true, false, true);
            AssertTrue("m6.dirtyMerge.baselineRemainsSilent",
                baselinePair.forceSilentBaseline);
            AssertTrue("m6.dirtyMerge.noInventedRemoval",
                !baselinePair.removedFaction);
        }

        private static void TestM6VisibilityAndExactIdentity()
        {
            KnowledgeRelationVisibilityInput visible = new KnowledgeRelationVisibilityInput
            {
                candidateHasName = true,
                candidateEverSeenByPlayer = true
            };
            AssertTrue("m6.visibility.direct", KnowledgeRelationPolicy.IsKnownVisibleRelation(visible));
            visible.candidateEverSeenByPlayer = false;
            AssertTrue("m6.visibility.hiddenUnseen",
                !KnowledgeRelationPolicy.IsKnownVisibleRelation(visible));
            visible.candidateEverSeenByPlayer = true;
            visible.candidateHidesRelations = true;
            AssertTrue("m6.visibility.hiddenCandidate",
                !KnowledgeRelationPolicy.IsKnownVisibleRelation(visible));
            visible.candidateHidesRelations = false;
            visible.ownerHidesRelations = true;
            AssertTrue("m6.visibility.hiddenOwner",
                !KnowledgeRelationPolicy.IsKnownVisibleRelation(visible));
            visible.ownerHidesRelations = false;
            visible.candidateNameIsNumerical = true;
            AssertTrue("m6.visibility.numerical",
                !KnowledgeRelationPolicy.IsKnownVisibleRelation(visible));
            visible.candidateNameIsNumerical = false;
            visible.candidateIsDeadAnimalWithoutCorpse = true;
            AssertTrue("m6.visibility.missingDeadAnimal",
                !KnowledgeRelationPolicy.IsKnownVisibleRelation(visible));
            visible.candidateIsDeadAnimalWithoutCorpse = false;
            AssertTrue("m6.knownness.visibleAloneNotEnough",
                !KnowledgeRelationPolicy.IsKnownSocialEntry(visible));
            visible.hasKnownRelation = true;
            AssertTrue("m6.knownness.exactRelation",
                KnowledgeRelationPolicy.IsKnownSocialEntry(visible));
            visible.hasKnownRelation = false;
            visible.candidateIsHumanlike = true;
            visible.sharesSocialContext = true;
            visible.ownerOpinionOfCandidate = 1;
            AssertTrue("m6.knownness.socialCardOpinion",
                KnowledgeRelationPolicy.IsKnownSocialEntry(visible));
            visible.ownerOpinionOfCandidate = 0;
            AssertTrue("m6.knownness.zeroOpinionHidden",
                !KnowledgeRelationPolicy.IsKnownSocialEntry(visible));
            visible.candidateOpinionOfOwner = -1;
            AssertTrue("m6.knownness.inboundOpinion",
                KnowledgeRelationPolicy.IsKnownSocialEntry(visible));
            visible.sharesSocialContext = false;
            AssertTrue("m6.knownness.offContextNotDiscovered",
                !KnowledgeRelationPolicy.IsKnownSocialEntry(visible));
            visible.candidateOpinionOfOwner = 0;
            visible.previouslyKnown = true;
            AssertTrue("m6.knownness.savedEdgeReconciles",
                KnowledgeRelationPolicy.IsKnownSocialEntry(visible));

            AssertTrue("m6.knownness.relativeSameMapExact",
                KnowledgeRelationPolicy.CanDiscloseExactRelativeLocation(true, false, false));
            AssertTrue("m6.knownness.relativeSameCaravanExact",
                KnowledgeRelationPolicy.CanDiscloseExactRelativeLocation(false, true, false));
            AssertTrue("m6.knownness.relativeCorpseOnOwnerMapExact",
                KnowledgeRelationPolicy.CanDiscloseExactRelativeLocation(false, false, true));
            AssertTrue("m6.knownness.offContextRelativeLocationUnknown",
                !KnowledgeRelationPolicy.CanDiscloseExactRelativeLocation(false, false, false));

            KnowledgeFactionState priorFaction = new KnowledgeFactionState
            {
                goodwill = 10,
                relationKindToken = KnowledgeObservationTokens.FactionRelationNeutral,
                leaderPawnId = "Pawn_LeaderA"
            };
            KnowledgeFactionState goodwillDrift = new KnowledgeFactionState
            {
                goodwill = 11,
                relationKindToken = KnowledgeObservationTokens.FactionRelationNeutral,
                leaderPawnId = "Pawn_LeaderA"
            };
            AssertTrue("m6.knownness.goodwillPointDriftNotEpisode",
                !KnowledgeRelationPolicy.IsFactionDiplomacyEpisode(
                    priorFaction, goodwillDrift));
            goodwillDrift.relationKindToken = KnowledgeObservationTokens.FactionRelationAlly;
            AssertTrue("m6.knownness.relationTierChangeIsEpisode",
                KnowledgeRelationPolicy.IsFactionDiplomacyEpisode(
                    priorFaction, goodwillDrift));
            goodwillDrift.relationKindToken = KnowledgeObservationTokens.FactionRelationNeutral;
            goodwillDrift.leaderPawnId = "Pawn_LeaderB";
            AssertTrue("m6.knownness.leaderChangeIsEpisode",
                KnowledgeRelationPolicy.IsFactionDiplomacyEpisode(
                    priorFaction, goodwillDrift));

            AssertTrue("m6.ownerAttach.firstEligibleIsSilent",
                KnowledgeRelationPolicy.OwnerAttachmentNeedsSilentBaseline(false, true));
            AssertTrue("m6.ownerAttach.alreadyAttachedIsOrdinary",
                !KnowledgeRelationPolicy.OwnerAttachmentNeedsSilentBaseline(true, true));
            AssertTrue("m6.ownerAttach.ineligibleDoesNotAttach",
                !KnowledgeRelationPolicy.OwnerAttachmentNeedsSilentBaseline(false, false));
            AssertTrue("m6.ownerAdmission.belowCap",
                KnowledgeRelationPolicy.CanAdmitObservationOwner(999, 1000));
            AssertTrue("m6.ownerAdmission.atCap",
                !KnowledgeRelationPolicy.CanAdmitObservationOwner(1000, 1000));
            AssertTrue("m6.ownerAdmission.bothDirectoriesHaveHeadroom",
                KnowledgeRelationPolicy.CanAdmitObservationOwner(
                    999, 1000, 1000, 1001));
            AssertTrue("m6.ownerAdmission.unionCapRefusesBelowActiveCap",
                !KnowledgeRelationPolicy.CanAdmitObservationOwner(
                    999, 1000, 1001, 1001));
            AssertTrue("m6.ownerAdmission.cultureOnlyDoesNotCount",
                !KnowledgeRelationPolicy.CountsAsActiveObservationOwner(
                    true, false, false, string.Empty));
            AssertTrue("m6.ownerAdmission.enrolledCurrentCounts",
                KnowledgeRelationPolicy.CountsAsActiveObservationOwner(
                    true, false, false, M6Epoch("Pawn_count")));
            AssertTrue("m6.ownerAdmission.legacyNeverCounts",
                !KnowledgeRelationPolicy.CountsAsActiveObservationOwner(
                    false, false, false, M6Epoch("Pawn_legacy_count")));
            AssertTrue("m6.ownerAdmission.archiveAndFenceDoNotCount",
                !KnowledgeRelationPolicy.CountsAsActiveObservationOwner(
                    true, true, false, M6Epoch("Pawn_archive"))
                && !KnowledgeRelationPolicy.CountsAsActiveObservationOwner(
                    true, false, true, M6Epoch("Pawn_fence")));
            AssertTrue("m6.ownerAdmission.fenceCountsOnlyInUnion",
                KnowledgeRelationPolicy.CountsAsNonArchiveEpochOwner(
                    true, false, M6Epoch("Pawn_fence_union"))
                && !KnowledgeRelationPolicy.CountsAsNonArchiveEpochOwner(
                    true, true, M6Epoch("Pawn_archive_union"))
                && !KnowledgeRelationPolicy.CountsAsNonArchiveEpochOwner(
                    true, false, string.Empty));

            KnowledgeOwnerRepairRevisionPlan ordinaryRepair =
                KnowledgeRelationPolicy.PlanOwnerRepairRevision(7, true);
            AssertTrue("m6.repair.ownerRevisionAdvances",
                ordinaryRepair.valid && ordinaryRepair.canCommit
                    && ordinaryRepair.nextRevision == 8);
            KnowledgeOwnerRepairRevisionPlan unchangedRepair =
                KnowledgeRelationPolicy.PlanOwnerRepairRevision(7, false);
            AssertTrue("m6.repair.ownerRevisionUnchanged",
                unchangedRepair.valid && !unchangedRepair.canCommit
                    && unchangedRepair.nextRevision == 7);
            KnowledgeOwnerRepairRevisionPlan zeroRevisionRepair =
                KnowledgeRelationPolicy.PlanOwnerRepairRevision(0, true);
            AssertTrue("m6.repair.ownerRevisionZeroInvalid",
                !zeroRevisionRepair.valid && !zeroRevisionRepair.canCommit);
            KnowledgeOwnerRepairRevisionPlan negativeRevisionRepair =
                KnowledgeRelationPolicy.PlanOwnerRepairRevision(-1, true);
            AssertTrue("m6.repair.ownerRevisionNegativeInvalid",
                !negativeRevisionRepair.valid && !negativeRevisionRepair.canCommit);
            KnowledgeOwnerRepairRevisionPlan finalRepair =
                KnowledgeRelationPolicy.PlanOwnerRepairRevision(long.MaxValue - 1, true);
            AssertTrue("m6.repair.ownerRevisionFinalAdvance",
                finalRepair.canCommit && finalRepair.nextRevision == long.MaxValue);
            KnowledgeOwnerRepairRevisionPlan saturatedRepair =
                KnowledgeRelationPolicy.PlanOwnerRepairRevision(long.MaxValue, true);
            AssertTrue("m6.repair.ownerRevisionSaturated",
                saturatedRepair.valid && !saturatedRepair.canCommit
                    && saturatedRepair.nextRevision == long.MaxValue);
            List<string> prioritized = KnowledgeRelationPolicy.PrioritizeObservationCandidateIds(
                new[] { "Pawn_Z" }, new[] { "Pawn_B", "Pawn_A" }, 2);
            AssertEqual("m6.candidates.savedEdgeFirst", "Pawn_Z", prioritized[0]);
            AssertEqual("m6.candidates.discoveredLexicalFill", "Pawn_A", prioritized[1]);

            string epoch = M6Epoch("Pawn_ab");
            string first;
            string second;
            AssertTrue("m6.identity.awareness.first", KnowledgeRelationPolicy.TryCreateAwarenessId(
                "Pawn_ab", epoch, KnowledgeObservationTokens.ScopeRelationship,
                KnowledgeObservationTokens.SubjectPawn, "Pawn_c",
                KnowledgeObservationTokens.StreamDirectedSocial, out first));
            AssertTrue("m6.identity.awareness.second", KnowledgeRelationPolicy.TryCreateAwarenessId(
                "Pawn_a", epoch, KnowledgeObservationTokens.ScopeRelationship,
                KnowledgeObservationTokens.SubjectPawn, "Pawn_bc",
                KnowledgeObservationTokens.StreamDirectedSocial, out second));
            AssertTrue("m6.identity.segmentCollision", !string.Equals(first, second, StringComparison.Ordinal));
            string parsedOwner;
            string parsedEpoch;
            string parsedSubjectKind;
            string parsedSubjectId;
            AssertTrue("m6.identity.awareness.parse",
                KnowledgeRelationPolicy.TryParseAwarenessId(
                    first, out parsedOwner, out parsedEpoch,
                    out parsedSubjectKind, out parsedSubjectId));
            AssertEqual("m6.identity.awareness.parseOwner", "Pawn_ab", parsedOwner);
            AssertEqual("m6.identity.awareness.parseEpoch", epoch, parsedEpoch);
            AssertEqual("m6.identity.awareness.parseSubjectKind",
                KnowledgeObservationTokens.SubjectPawn, parsedSubjectKind);
            AssertEqual("m6.identity.awareness.parseSubjectId", "Pawn_c", parsedSubjectId);
            AssertTrue("m6.identity.awareness.trailingRejected",
                !KnowledgeRelationPolicy.TryParseAwarenessId(
                    first + "1:x", out parsedOwner, out parsedEpoch,
                    out parsedSubjectKind, out parsedSubjectId));

            string forward;
            string reverse;
            AssertTrue("m6.identity.pair.forward",
                KnowledgeRelationPolicy.TryCreateDirectedPairKey("Pawn_A", "Pawn_B", out forward));
            AssertTrue("m6.identity.pair.reverse",
                KnowledgeRelationPolicy.TryCreateDirectedPairKey("Pawn_B", "Pawn_A", out reverse));
            AssertTrue("m6.identity.pairDirected", !string.Equals(forward, reverse, StringComparison.Ordinal));

            string setA;
            string setB;
            string setSorted;
            AssertTrue("m6.identity.setA", KnowledgeRelationPolicy.TryEncodeRelationDefSet(
                new[] { "ab", "c" }, 128, out setA));
            AssertTrue("m6.identity.setB", KnowledgeRelationPolicy.TryEncodeRelationDefSet(
                new[] { "a", "bc" }, 128, out setB));
            AssertTrue("m6.identity.setNoCollision", setA != setB);
            AssertTrue("m6.identity.setSorted", KnowledgeRelationPolicy.TryEncodeRelationDefSet(
                new[] { "Spouse", "Parent", "Spouse" }, 128, out setSorted));
            string sortedAgain;
            KnowledgeRelationPolicy.TryEncodeRelationDefSet(
                new[] { "Parent", "Spouse" }, 128, out sortedAgain);
            AssertEqual("m6.identity.setDedup", sortedAgain, setSorted);
            AssertTrue("m6.identity.setDuplicateInspectionBoundary",
                KnowledgeRelationPolicy.TryEncodeRelationDefSet(
                    Enumerable.Repeat("Spouse", 128), 128, out setSorted));
            AssertTrue("m6.identity.setDuplicateInspectionOverflow",
                !KnowledgeRelationPolicy.TryEncodeRelationDefSet(
                    Enumerable.Repeat("Spouse", 129), 128, out setSorted));

            KnowledgeObservationPolicySnapshot familyPolicy =
                new KnowledgeObservationPolicySnapshot().Normalized();
            AssertTrue("m6.family.nullPolicyFallback",
                KnowledgeRelationPolicy.IsFamilyRelation(
                    false, false, "Stepparent", null));
            AssertTrue("m6.family.blood",
                KnowledgeRelationPolicy.IsFamilyRelation(true, false, "Unknown", familyPolicy));
            AssertTrue("m6.family.spouse",
                KnowledgeRelationPolicy.IsFamilyRelation(false, true, "Spouse", familyPolicy));
            foreach (string relation in new[]
                     { "Stepparent", "Stepchild", "ParentInLaw", "ChildInLaw" })
            {
                AssertTrue("m6.family.choice." + relation,
                    KnowledgeRelationPolicy.IsFamilyRelation(
                        false, false, relation, familyPolicy));
            }
            AssertTrue("m6.family.unknownSocialOnly",
                !KnowledgeRelationPolicy.IsFamilyRelation(
                    false, false, "ExLover", familyPolicy));
            KnowledgeObservationPolicySnapshot oversizedFamilyPolicy =
                new KnowledgeObservationPolicySnapshot
                {
                    familyRelationDefNames = Enumerable.Range(0, 17)
                        .Select(index => "FamilyChoice" + index)
                        .ToList()
                }.Normalized();
            AssertEqual("m6.family.oversizedFallback", 4,
                oversizedFamilyPolicy.familyRelationDefNames.Count);
        }

        private static void TestObservationIndexRefreshArchitecture()
        {
            string source = File.ReadAllText(Path.Combine(
                RepoRoot(), "Source", "Core", "DiaryGameComponent.MemoryObservation.cs"));
            AssertEqual("m6.performance.factualRefreshUsesPublishedIndexes", 3,
                TextOccurrences(
                    source,
                    "if (factualAdmitted) PublishMemoryObservationBudgetTotals(budget);"));
            AssertEqual("m6.performance.fullRebuildReservedForMissingBudgetOwner", 1,
                TextOccurrences(source, "RefreshMemoryObservationBudgetSession(budget);"));
            AssertEqual("m6.performance.boundedRebuildSites", 4,
                TextOccurrences(source, "RebuildMemorySizeIndexes();"));
            AssertEqual("m6.performance.no-monolithic-source-copy", 0,
                TextOccurrences(source, "TryCopyBoundedMemoryObservationSource"));
            AssertTrue("m6.performance.source-copy-uses-cursor",
                source.Contains("AdvanceBoundedMemoryObservationSourceCopy("));
        }

        private static void TestObservationSourceCopySlices()
        {
            MemoryObservationSourceCopyPlan first = MemoryObservationSourceCopyPolicy.Plan(
                100, 0, 0, 100, 30);
            AssertTrue("m6.sourceCopy.first.valid",
                first.valid && !first.complete && !first.overflow);
            AssertEqual("m6.sourceCopy.first.count", 30, first.workItems);
            AssertEqual("m6.sourceCopy.first.next", 30, first.nextIndex);

            MemoryObservationSourceCopyPlan last = MemoryObservationSourceCopyPolicy.Plan(
                100, 90, 90, 100, 30);
            AssertTrue("m6.sourceCopy.last.complete", last.valid && last.complete);
            AssertEqual("m6.sourceCopy.last.count", 10, last.workItems);
            AssertEqual("m6.sourceCopy.last.next", 100, last.nextIndex);

            MemoryObservationSourceCopyPlan overflow = MemoryObservationSourceCopyPolicy.Plan(
                101, 0, 0, 100, 30);
            AssertTrue("m6.sourceCopy.overflow",
                overflow.valid && overflow.overflow && overflow.complete
                    && overflow.workItems == 0);
            AssertTrue("m6.sourceCopy.cursorMismatchRefuses",
                !MemoryObservationSourceCopyPolicy.Plan(100, 29, 30, 100, 30).valid);
            AssertTrue("m6.sourceCopy.zeroSourceComplete",
                MemoryObservationSourceCopyPolicy.Plan(0, 0, 0, 100, 30).complete);
        }

        private static void TestMigrationUsesRunningBudgetProjection()
        {
            string source = File.ReadAllText(Path.Combine(
                RepoRoot(), "Source", "Core", "DiaryGameComponent.MemoryMigration.cs"));
            int start = source.IndexOf(
                "internal void RunMemoryMigrationCommit()", StringComparison.Ordinal);
            int end = source.IndexOf(
                "private static bool GroupRequiresLegacyMigration", start, StringComparison.Ordinal);
            AssertTrue("migration.performance.method-found", start >= 0 && end > start);
            string method = source.Substring(start, end - start);
            AssertEqual("migration.performance.bounded-full-rebuilds", 3,
                TextOccurrences(method, "RebuildMemorySizeIndexes();"));
            AssertTrue("migration.performance.running-budget-created",
                method.Contains("CreateMemoryMigrationBudgetSession()"));
            AssertTrue("migration.performance.running-budget-advanced",
                method.Contains("ApplyMemoryMigrationBudgetProjection("));
            AssertTrue("migration.performance.no-per-owner-rebuild",
                !method.Contains("PublishLegacyOwnerCommit(group.Value, commit);\n"
                    + "                    MarkMemoryM4IndexesDirty();"));
        }

        private static void TestRoutineAdmissionUsesOwnerLocalCopiesAndIndexes()
        {
            string source = File.ReadAllText(Path.Combine(
                RepoRoot(), "Source", "Core", "DiaryGameComponent.MemoryM4Store.cs"));
            int start = source.IndexOf(
                "internal MemoryStoreAdmissionResult TryAdmitMemoryBlock(",
                StringComparison.Ordinal);
            int end = source.IndexOf(
                "internal bool TryReduceSavedMemoryRoot(",
                start,
                StringComparison.Ordinal);
            AssertTrue("m4.performance.admission-method-found", start >= 0 && end > start);
            string method = source.Substring(start, end - start);
            AssertEqual("m4.performance.no-whole-root-clone", 0,
                TextOccurrences(method, "CloneSavedRoots(state.threadRoots)"));
            AssertEqual("m4.performance.no-unconditional-global-m4-rebuild", 0,
                TextOccurrences(method, "RebuildMemoryM4Indexes();"));
            AssertTrue("m4.performance.owner-local-m4-reindex",
                method.Contains("ReindexMemoryM4OwnerAfterCommit("));
            AssertTrue("m4.performance.owner-local-byte-refresh",
                method.Contains("RefreshMemorySizeIndexForOwner("));
        }

        private static void TestDispatchUsesOwnerLocalByteRefresh()
        {
            string source = File.ReadAllText(Path.Combine(
                RepoRoot(), "Source", "Core", "DiaryGameComponent.MemoryDispatch.cs"));
            int start = source.IndexOf(
                "private MemoryInvocationCommitPermitV1 TryCommitMemoryInvocation(",
                StringComparison.Ordinal);
            int helper = source.IndexOf(
                "private void RefreshMemoryDispatchSizeIndex(",
                start,
                StringComparison.Ordinal);
            int end = source.IndexOf(
                "private static bool TransportIdentityMatches(",
                helper,
                StringComparison.Ordinal);
            AssertTrue("m2.performance.dispatch-methods-found",
                start >= 0 && helper > start && end > helper);
            string routine = source.Substring(start, helper - start);
            string fallback = source.Substring(helper, end - helper);
            AssertEqual("m2.performance.no-routine-global-byte-rebuild", 0,
                TextOccurrences(routine, "RebuildMemorySizeIndexes();"));
            AssertEqual("m2.performance.owner-local-refresh-sites", 4,
                TextOccurrences(routine, "RefreshMemoryDispatchSizeIndex("));
            AssertTrue("m2.performance.owner-local-byte-refresh",
                fallback.Contains("RefreshMemorySizeIndexForOwner(owner)"));
            AssertEqual("m2.performance.corrupt-index-fallback-only", 1,
                TextOccurrences(fallback, "RebuildMemorySizeIndexes();"));
        }

        private static void TestRuntimeMemoryWiringArchitecture()
        {
            string root = RepoRoot();
            string library = File.ReadAllText(Path.Combine(
                root, "Source", "UI", "Dialog_MemoryLibrary.cs"));
            int windowStart = library.IndexOf(
                "public override void WindowUpdate()", StringComparison.Ordinal);
            int windowEnd = library.IndexOf(
                "public override void PostClose()", windowStart, StringComparison.Ordinal);
            AssertTrue("runtime.library.window-update-found",
                windowStart >= 0 && windowEnd > windowStart);
            string window = library.Substring(windowStart, windowEnd - windowStart);
            int poll = window.IndexOf(
                "MemoryLibraryUiPollPolicy.ShouldPoll(", StringComparison.Ordinal);
            int owners = window.IndexOf("RefreshOwners();", StringComparison.Ordinal);
            AssertTrue("runtime.library.poll-before-repository",
                poll >= 0 && owners > poll);
            AssertEqual("runtime.library.one-poll-gate", 1,
                TextOccurrences(window, "MemoryLibraryUiPollPolicy.ShouldPoll("));

            string maintenance = File.ReadAllText(Path.Combine(
                root, "Source", "Core", "DiaryGameComponent.MemoryMaintenance.cs"));
            int sliceStart = maintenance.IndexOf(
                "private void RunMemoryMaintenanceSlice(int nowTick)",
                StringComparison.Ordinal);
            int sliceEnd = maintenance.IndexOf(
                "private void RebuildMemoryMaintenanceHandles()",
                sliceStart,
                StringComparison.Ordinal);
            AssertTrue("runtime.maintenance.slice-found",
                sliceStart >= 0 && sliceEnd > sliceStart);
            string slice = maintenance.Substring(sliceStart, sliceEnd - sliceStart);
            int timer = slice.IndexOf("Stopwatch.StartNew()", StringComparison.Ordinal);
            AssertTrue("runtime.maintenance.timer-before-index-preparation",
                timer >= 0
                    && slice.IndexOf("EnsureMemoryM4Indexes();", StringComparison.Ordinal) > timer);
            AssertTrue("runtime.maintenance.timer-before-handle-preparation",
                slice.IndexOf("RebuildMemoryMaintenanceHandles();", StringComparison.Ordinal) > timer);
            AssertTrue("runtime.maintenance.timer-before-legacy-preparation",
                slice.IndexOf("ApplyKnowledgeEviction();", StringComparison.Ordinal) > timer);
            int pressureStart = maintenance.IndexOf(
                "private bool TryCompleteMemoryMaintenanceCycle(", StringComparison.Ordinal);
            int completionStart = maintenance.IndexOf(
                "private void CompleteMemoryMaintenanceCycle(", pressureStart,
                StringComparison.Ordinal);
            int elapsedStart = maintenance.IndexOf(
                "private static long ElapsedMicroseconds(", completionStart,
                StringComparison.Ordinal);
            AssertTrue("runtime.maintenance.pressure-methods-found",
                pressureStart >= 0 && completionStart > pressureStart && elapsedStart > completionStart);
            string pressure = maintenance.Substring(pressureStart, completionStart - pressureStart);
            string completion = maintenance.Substring(completionStart, elapsedStart - completionStart);
            AssertTrue("runtime.maintenance.pressure-budget-checked",
                pressure.Contains("MemoryMaintenancePolicy.ShouldDeferFinalPressure("));
            AssertTrue("runtime.maintenance.pressure-inside-budgeted-method",
                pressure.Contains("TryApplyMemoryPressureCaps(nowTick)"));
            AssertEqual("runtime.maintenance.no-hidden-final-pressure", 0,
                TextOccurrences(completion, "TryApplyMemoryPressureCaps("));

            string recall = File.ReadAllText(Path.Combine(
                root, "Source", "Core", "DiaryGameComponent.MemoryRecallV2.cs"));
            AssertTrue("runtime.recall.current-projection-policy-wired",
                recall.Contains("MemoryThreadLookupPolicy.UseRollingCurrentProjection("));

            string knowledge = File.ReadAllText(Path.Combine(
                root, "Source", "Core", "DiaryGameComponent.Knowledge.cs"));
            int ensureStart = knowledge.IndexOf(
                "private PawnKnowledgeState EnsureKnowledgeState(PawnDiaryRecord diary)",
                StringComparison.Ordinal);
            int ensureEnd = knowledge.IndexOf(
                "// ── Persist drafts", ensureStart, StringComparison.Ordinal);
            AssertTrue("runtime.old-save.ensure-knowledge-found",
                ensureStart >= 0 && ensureEnd > ensureStart);
            string ensure = knowledge.Substring(ensureStart, ensureEnd - ensureStart);
            AssertTrue("runtime.old-save.current-invariants-created",
                ensure.Contains("PawnKnowledgeState.CreateCurrent("));

            string coreSource = string.Join("\n", Directory.GetFiles(
                    Path.Combine(root, "Source", "Core"), "*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));
            AssertEqual("runtime.budget.single-limit-construction", 1,
                TextOccurrences(coreSource, "new MemoryBudgetLimits"));
            AssertTrue("runtime.budget.all-steady-state-sites-use-reserve-helper",
                TextOccurrences(coreSource, "CurrentMemoryBudgetLimits()") >= 6);

            string dispatch = File.ReadAllText(Path.Combine(
                root, "Source", "Core", "DiaryGameComponent.MemoryDispatch.cs"));
            AssertTrue("runtime.library.status-mutations-invalidate-cache",
                TextOccurrences(dispatch, "MarkMemoryLibraryStatusProjectionDirty();") >= 4);

            string observation = File.ReadAllText(Path.Combine(
                root, "Source", "Core", "DiaryGameComponent.MemoryObservation.cs"));
            AssertTrue("runtime.knownness.relative-location-policy-wired",
                observation.Contains(
                    "KnowledgeRelationPolicy.CanDiscloseExactRelativeLocation("));
            AssertTrue("runtime.knownness.faction-diplomacy-policy-wired",
                observation.Contains(
                    "KnowledgeRelationPolicy.IsFactionDiplomacyEpisode("));
        }

        private static void TestM6FactCanonicalization()
        {
            KnowledgeObservationPolicySnapshot policy =
                new KnowledgeObservationPolicySnapshot().Normalized();
            List<KnowledgeStateFact> normalized;
            AssertTrue("m6.facts.equalDuplicateCollapses",
                KnowledgeRelationPolicy.TryNormalizeStateFacts(
                    new[]
                    {
                        new KnowledgeStateFact
                        {
                            key = KnowledgeObservationTokens.FactConnectionKind,
                            value = KnowledgeObservationTokens.ConnectionFamily
                        },
                        new KnowledgeStateFact
                        {
                            key = KnowledgeObservationTokens.FactConnectionKind,
                            value = KnowledgeObservationTokens.ConnectionFamily
                        }
                    },
                    KnowledgeObservationTokens.ScopeFaction,
                    KnowledgeObservationTokens.SubjectFaction,
                    KnowledgeObservationTokens.StreamFactionConnection,
                    policy,
                    out normalized));
            AssertEqual("m6.facts.equalDuplicateOne", 1, normalized.Count);

            AssertTrue("m6.facts.conflictRejected",
                !KnowledgeRelationPolicy.TryNormalizeStateFacts(
                    new[]
                    {
                        new KnowledgeStateFact
                        {
                            key = KnowledgeObservationTokens.FactConnectionKind,
                            value = KnowledgeObservationTokens.ConnectionFamily
                        },
                        new KnowledgeStateFact
                        {
                            key = KnowledgeObservationTokens.FactConnectionKind,
                            value = KnowledgeObservationTokens.ConnectionCurrent
                        }
                    },
                    KnowledgeObservationTokens.ScopeFaction,
                    KnowledgeObservationTokens.SubjectFaction,
                    KnowledgeObservationTokens.StreamFactionConnection,
                    policy,
                    out normalized));
            AssertEqual("m6.facts.conflictClears", 0, normalized.Count);

            string relationSet;
            KnowledgeRelationPolicy.TryEncodeRelationDefSet(
                new[] { "Parent", "Spouse" }, 128, out relationSet);
            AssertTrue("m6.facts.canonicalRelationSet",
                KnowledgeRelationPolicy.TryNormalizeStateFacts(
                    new[]
                    {
                        new KnowledgeStateFact
                        {
                            key = KnowledgeObservationTokens.FactRelationDefs,
                            value = relationSet
                        }
                    },
                    KnowledgeObservationTokens.ScopeRelative,
                    KnowledgeObservationTokens.SubjectPawn,
                    KnowledgeObservationTokens.StreamRelativeState,
                    policy,
                    out normalized));
            AssertTrue("m6.facts.trailingRelationBytesRejected",
                !KnowledgeRelationPolicy.TryNormalizeStateFacts(
                    new[]
                    {
                        new KnowledgeStateFact
                        {
                            key = KnowledgeObservationTokens.FactRelationDefs,
                            value = relationSet + "x"
                        }
                    },
                    KnowledgeObservationTokens.ScopeRelative,
                    KnowledgeObservationTokens.SubjectPawn,
                    KnowledgeObservationTokens.StreamRelativeState,
                    policy,
                    out normalized));

            List<KnowledgeStateFact> opinion = new List<KnowledgeStateFact>
            {
                new KnowledgeStateFact
                {
                    key = KnowledgeObservationTokens.FactOpinionBand,
                    value = KnowledgeObservationTokens.OpinionNeutral
                },
                new KnowledgeStateFact
                {
                    key = KnowledgeObservationTokens.FactOpinionValue,
                    value = "0"
                }
            };
            AssertTrue("m6.facts.opinionEpisodeExact",
                KnowledgeRelationPolicy.TryNormalizeOpinionEpisodeFacts(
                    opinion, policy, out normalized));
            AssertEqual("m6.facts.opinionEpisodeSortedFirst",
                KnowledgeObservationTokens.FactOpinionBand, normalized[0].key);
            opinion[1].value = "+0";
            AssertTrue("m6.facts.noncanonicalOpinionRejected",
                !KnowledgeRelationPolicy.TryNormalizeOpinionEpisodeFacts(
                    opinion, policy, out normalized));
            opinion.RemoveAt(1);
            AssertTrue("m6.facts.incompleteEpisodeRejected",
                !KnowledgeRelationPolicy.TryNormalizeOpinionEpisodeFacts(
                    opinion, policy, out normalized));

            string factionSubject;
            MemoryIdentityCodec.TryCreateFactionSubjectId("Faction_17", 1, out factionSubject);
            AssertTrue("m6.subject.exactFactionAccepted",
                KnowledgeRelationPolicy.IsValidObservationSubject(
                    KnowledgeObservationTokens.SubjectFaction, factionSubject));
            AssertTrue("m6.subject.factionLabelRejected",
                !KnowledgeRelationPolicy.IsValidObservationSubject(
                    KnowledgeObservationTokens.SubjectFaction, "Same label"));
            KnowledgeAwarenessState relative = new KnowledgeAwarenessState
            {
                scopeKindToken = KnowledgeObservationTokens.ScopeRelative,
                trackingStateToken = KnowledgeObservationTokens.TrackingTracked,
                stateFacts = new List<KnowledgeStateFact>
                {
                    new KnowledgeStateFact
                    {
                        key = KnowledgeObservationTokens.FactFactionSubject,
                        value = factionSubject
                    }
                }
            };
            AssertTrue("m6.familyFaction.liveRelativeRetains",
                !KnowledgeRelationPolicy.CanPruneFamilyFactionConnection(
                    factionSubject, new[] { relative }));
            relative.stateFacts[0].value = "none";
            AssertTrue("m6.familyFaction.absentRelativePrunes",
                KnowledgeRelationPolicy.CanPruneFamilyFactionConnection(
                    factionSubject, new[] { relative }));
            relative.trackingStateToken = KnowledgeObservationTokens.TrackingCapacityUntracked;
            AssertTrue("m6.familyFaction.untrackedRelativeFailsClosed",
                !KnowledgeRelationPolicy.CanPruneFamilyFactionConnection(
                    factionSubject, new[] { relative }));
            AssertTrue("m6.stream.scopeMismatchRejected",
                !KnowledgeRelationPolicy.IsKnownObservationStreamShape(
                    KnowledgeObservationTokens.ScopeRelative,
                    KnowledgeObservationTokens.SubjectPawn,
                    KnowledgeObservationTokens.StreamDirectedSocial));
        }

        private static void TestM6DuplicateRepairPolicy()
        {
            string epoch = M6Epoch("Pawn_A");
            string awarenessId;
            KnowledgeRelationPolicy.TryCreateAwarenessId(
                "Pawn_A", epoch, KnowledgeObservationTokens.ScopeRelative,
                KnowledgeObservationTokens.SubjectPawn, "Pawn_B",
                KnowledgeObservationTokens.StreamRelativeState, out awarenessId);
            KnowledgeAwarenessState first = new KnowledgeAwarenessState
            {
                snapshotId = awarenessId,
                scopeKindToken = KnowledgeObservationTokens.ScopeRelative,
                subjectKind = KnowledgeObservationTokens.SubjectPawn,
                subjectId = "Pawn_B",
                factStreamToken = KnowledgeObservationTokens.StreamRelativeState,
                captureInvalidationGeneration = 1,
                knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceDirect,
                stateFacts = new List<KnowledgeStateFact>
                {
                    new KnowledgeStateFact
                    {
                        key = KnowledgeObservationTokens.FactLifeState,
                        value = KnowledgeObservationTokens.LifeAlive
                    }
                },
                firstObservedTick = 20,
                lastObservedTick = 100,
                trackingStateToken = KnowledgeObservationTokens.TrackingTracked,
                snapshotRevision = 4
            };
            KnowledgeAwarenessState conflicting = M6AwarenessCopy(first);
            conflicting.firstObservedTick = 10;
            KnowledgeAwarenessRepairPlan awarenessConflict =
                KnowledgeRelationPolicy.PlanAwarenessDuplicateRepair(
                    new[] { first, conflicting }, false, 7);
            AssertTrue("m6.repair.awarenessConflict", awarenessConflict.conflict);
            AssertEqual("m6.repair.awarenessCurrentGeneration", 7L,
                awarenessConflict.repairMarker.captureInvalidationGeneration);
            AssertEqual("m6.repair.awarenessMinimumFirst", 10L,
                awarenessConflict.repairMarker.firstObservedTick);
            AssertEqual("m6.repair.awarenessNoFacts", 0,
                awarenessConflict.repairMarker.stateFacts.Count);
            conflicting.lastObservedTick = 99;
            KnowledgeAwarenessRepairPlan lowerRank =
                KnowledgeRelationPolicy.PlanAwarenessDuplicateRepair(
                    new[] { first, conflicting }, false, 7);
            AssertTrue("m6.repair.awarenessLowerRankCollapses",
                lowerRank.valid && !lowerRank.conflict && lowerRank.retainedIndex == 0);

            string pairKey;
            KnowledgeRelationPolicy.TryCreateDirectedPairKey("Pawn_A", "Pawn_B", out pairKey);
            string episodeId;
            KnowledgeRelationPolicy.TryCreateEpisodeId(
                "Pawn_A", epoch,
                KnowledgeObservationTokens.ScopeRelationship,
                KnowledgeObservationTokens.StreamDirectedSocial,
                KnowledgeObservationTokens.OpinionEpisodeRule,
                KnowledgeObservationTokens.OpinionEpisodeKind,
                KnowledgeObservationTokens.SubjectPawn,
                "Pawn_B", pairKey, KnowledgeObservationTokens.DirectionRising,
                out episodeId);
            string parsedOwner;
            string parsedEpoch;
            string parsedSubjectKind;
            string parsedSubjectId;
            AssertTrue("m6.identity.episode.parse",
                KnowledgeRelationPolicy.TryParseEpisodeId(
                    episodeId, out parsedOwner, out parsedEpoch,
                    out parsedSubjectKind, out parsedSubjectId));
            AssertEqual("m6.identity.episode.parseOwner", "Pawn_A", parsedOwner);
            AssertEqual("m6.identity.episode.parseEpoch", epoch, parsedEpoch);
            AssertEqual("m6.identity.episode.parseSubjectKind",
                KnowledgeObservationTokens.SubjectPawn, parsedSubjectKind);
            AssertEqual("m6.identity.episode.parseSubjectId", "Pawn_B", parsedSubjectId);
            AssertTrue("m6.identity.episode.trailingRejected",
                !KnowledgeRelationPolicy.TryParseEpisodeId(
                    episodeId + "1:x", out parsedOwner, out parsedEpoch,
                    out parsedSubjectKind, out parsedSubjectId));
            KnowledgeOpinionEpisodeState episode = new KnowledgeOpinionEpisodeState
            {
                episodeId = episodeId,
                captureRuleId = KnowledgeObservationTokens.OpinionEpisodeRule,
                scopeKindToken = KnowledgeObservationTokens.ScopeRelationship,
                factStreamToken = KnowledgeObservationTokens.StreamDirectedSocial,
                category = MemoryContractTokens.CategoryRelationships,
                captureInvalidationGeneration = 1,
                episodeKindToken = KnowledgeObservationTokens.OpinionEpisodeKind,
                subjectKind = KnowledgeObservationTokens.SubjectPawn,
                subjectId = "Pawn_B",
                pairOrStreamKey = pairKey,
                directionToken = KnowledgeObservationTokens.DirectionRising,
                baselineFacts = M6OpinionFacts(0),
                currentFacts = M6OpinionFacts(10),
                firstObservedTick = 100,
                lastObservedTick = 200,
                episodeRevision = 2
            };
            KnowledgeOpinionEpisodeState episodeConflict = M6EpisodeCopy(episode);
            episodeConflict.currentFacts = M6OpinionFacts(11);
            KnowledgeEpisodeRepairPlan episodeRepair =
                KnowledgeRelationPolicy.PlanEpisodeDuplicateRepair(
                    "Pawn_A", epoch, new[] { episode, episodeConflict }, false, 3);
            AssertTrue("m6.repair.episodeConflictDrops", episodeRepair.conflict
                && episodeRepair.retainedIndex < 0);
            string relationshipAwarenessId;
            KnowledgeRelationPolicy.TryCreateAwarenessId(
                "Pawn_A", epoch, KnowledgeObservationTokens.ScopeRelationship,
                KnowledgeObservationTokens.SubjectPawn, "Pawn_B",
                KnowledgeObservationTokens.StreamDirectedSocial,
                out relationshipAwarenessId);
            AssertEqual("m6.repair.episodeMarkerKey", relationshipAwarenessId,
                episodeRepair.repairMarker.snapshotId);
            AssertEqual("m6.repair.episodeMarkerGeneration", 3L,
                episodeRepair.repairMarker.captureInvalidationGeneration);
            KnowledgeAwarenessState episodeAwareness = new KnowledgeAwarenessState
            {
                snapshotId = relationshipAwarenessId,
                scopeKindToken = KnowledgeObservationTokens.ScopeRelationship,
                subjectKind = KnowledgeObservationTokens.SubjectPawn,
                subjectId = "Pawn_B",
                factStreamToken = KnowledgeObservationTokens.StreamDirectedSocial,
                captureInvalidationGeneration = 1,
                trackingStateToken = KnowledgeObservationTokens.TrackingTracked
            };
            AssertTrue("m6.repair.episodeTrackedPairRetained",
                KnowledgeRelationPolicy.IsEpisodeBackedByTrackedAwareness(
                    episodeAwareness, episode));
            AssertEqual("m6.repair.episodeTrackedDisposition",
                KnowledgeEpisodeBackingDisposition.Retain,
                KnowledgeRelationPolicy.EpisodeBackingDisposition(
                    episodeAwareness, episode, false));
            episodeAwareness.trackingStateToken =
                KnowledgeObservationTokens.TrackingCapacityUntracked;
            AssertTrue("m6.repair.episodeCapacityPairDropped",
                !KnowledgeRelationPolicy.IsEpisodeBackedByTrackedAwareness(
                    episodeAwareness, episode));
            episodeAwareness.trackingStateToken = KnowledgeObservationTokens.TrackingTracked;
            episodeAwareness.captureInvalidationGeneration = 2;
            AssertTrue("m6.repair.episodeGenerationMismatchDropped",
                !KnowledgeRelationPolicy.IsEpisodeBackedByTrackedAwareness(
                    episodeAwareness, episode));
            AssertTrue("m6.repair.episodeMissingAwarenessDropped",
                !KnowledgeRelationPolicy.IsEpisodeBackedByTrackedAwareness(null, episode));
            AssertEqual("m6.repair.episodeMissingConflictNoMarker",
                KnowledgeEpisodeBackingDisposition.DropWithoutMarker,
                KnowledgeRelationPolicy.EpisodeBackingDisposition(null, episode, true));
            AssertEqual("m6.repair.episodeExistingConflictMarker",
                KnowledgeEpisodeBackingDisposition.PublishConflictMarker,
                KnowledgeRelationPolicy.EpisodeBackingDisposition(
                    episodeAwareness, episode, true));

            KnowledgeFactionState faction = new KnowledgeFactionState
            {
                factionInstanceId = "Faction_17",
                allocatorGeneration = 4,
                factionDefName = "SameDef",
                frozenDisplayLabel = "Same label",
                goodwill = 10,
                relationKindToken = KnowledgeObservationTokens.FactionRelationNeutral,
                observedTick = 50,
                trackingStateToken = KnowledgeObservationTokens.TrackingTracked,
                snapshotRevision = 2
            };
            KnowledgeFactionState factionConflict = M6FactionCopy(faction);
            factionConflict.goodwill = 20;
            KnowledgeFactionRepairPlan factionRepair =
                KnowledgeRelationPolicy.PlanFactionDuplicateRepair(
                    new[] { faction, factionConflict }, false);
            AssertTrue("m6.repair.factionConflict", factionRepair.conflict);
            AssertEqual("m6.repair.factionExactInstance", "Faction_17",
                factionRepair.repairMarker.factionInstanceId);
            AssertEqual("m6.repair.factionMarker",
                KnowledgeObservationTokens.TrackingCapacityUntracked,
                factionRepair.repairMarker.trackingStateToken);
        }

        private static void TestM6SilentBaselinesAndCapacityMarkers()
        {
            KnowledgeObservationPolicySnapshot policy =
                new KnowledgeObservationPolicySnapshot().Normalized();
            string epoch = M6Epoch("Pawn_A");
            KnowledgeCurrentTruthObservation first = new KnowledgeCurrentTruthObservation
            {
                ownerPawnId = "Pawn_A",
                ownerEpochToken = epoch,
                scopeKindToken = KnowledgeObservationTokens.ScopeRelative,
                subjectKind = KnowledgeObservationTokens.SubjectPawn,
                subjectId = "Pawn_B",
                factStreamToken = KnowledgeObservationTokens.StreamRelativeState,
                captureInvalidationGeneration = 1,
                knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceDirect,
                stateFacts = new List<KnowledgeStateFact>
                {
                    new KnowledgeStateFact
                    {
                        key = KnowledgeObservationTokens.FactLifeState,
                        value = KnowledgeObservationTokens.LifeAlive
                    }
                },
                observedTick = 100,
                captureAllowed = true
            };
            KnowledgeAwarenessPlan baseline = KnowledgeRelationPolicy.PlanCurrentTruth(
                null, first, policy);
            AssertTrue("m6.baseline.valid", baseline.valid);
            AssertTrue("m6.baseline.silent", baseline.silentBaseline);
            AssertEqual("m6.baseline.tracked", KnowledgeObservationTokens.TrackingTracked,
                baseline.replacement.trackingStateToken);

            first.observedTick = 110;
            first.stateFacts[0].value = KnowledgeObservationTokens.LifeDead;
            KnowledgeAwarenessPlan changed = KnowledgeRelationPolicy.PlanCurrentTruth(
                baseline.replacement, first, policy);
            AssertTrue("m6.change.notBaseline", !changed.silentBaseline);
            AssertTrue("m6.change.detected", changed.authoritativeStateChanged);

            first.captureAllowed = false;
            first.observedTick = 120;
            KnowledgeAwarenessPlan disabled = KnowledgeRelationPolicy.PlanCurrentTruth(
                changed.replacement, first, policy);
            AssertTrue("m6.disabled.silent", disabled.silentBaseline);
            first.captureAllowed = true;
            first.captureInvalidationGeneration = 2;
            first.observedTick = 130;
            KnowledgeAwarenessPlan reenabled = KnowledgeRelationPolicy.PlanCurrentTruth(
                disabled.replacement, first, policy);
            AssertTrue("m6.reenabled.silentGenerationBaseline", reenabled.silentBaseline);
            AssertEqual("m6.reenabled.generation", 2L,
                reenabled.replacement.captureInvalidationGeneration);

            reenabled.replacement.snapshotRevision = long.MaxValue - 1;
            first.observedTick = 140;
            KnowledgeAwarenessPlan saturated = KnowledgeRelationPolicy.PlanCurrentTruth(
                reenabled.replacement, first, policy);
            AssertEqual("m6.saturation.marker",
                KnowledgeObservationTokens.TrackingCapacityUntracked,
                saturated.replacement.trackingStateToken);
            AssertEqual("m6.saturation.noFacts", 0, saturated.replacement.stateFacts.Count);
            AssertTrue("m6.saturation.silent", saturated.silentBaseline);
            long saturatedLastTick = saturated.replacement.lastObservedTick;
            first.observedTick = 145;
            KnowledgeAwarenessPlan terminalNoOp = KnowledgeRelationPolicy.PlanCurrentTruth(
                saturated.replacement, first, policy);
            AssertTrue("m6.saturation.terminalValid", terminalNoOp.valid);
            AssertTrue("m6.saturation.terminalNoSavedMutation",
                !terminalNoOp.savedMutationRequired);
            AssertEqual("m6.saturation.terminalTickStable",
                saturatedLastTick, terminalNoOp.replacement.lastObservedTick);
            AssertTrue("m6.saturation.maxMarkerNotRemovable",
                !KnowledgeRelationPolicy.CanRemoveShadowSnapshot(long.MaxValue));
            AssertTrue("m6.saturation.preterminalRemovable",
                KnowledgeRelationPolicy.CanRemoveShadowSnapshot(long.MaxValue - 1));

            first.stateFacts[0].value = new string('x', 129);
            first.observedTick = 150;
            KnowledgeAwarenessPlan oversized = KnowledgeRelationPolicy.PlanCurrentTruth(
                null, first, policy);
            AssertEqual("m6.oversize.marker",
                KnowledgeObservationTokens.TrackingCapacityUntracked,
                oversized.replacement.trackingStateToken);
            AssertEqual("m6.oversize.noPartialFacts", 0, oversized.replacement.stateFacts.Count);
        }

        private static void TestM6DeterministicOpinionEpisodes()
        {
            KnowledgeObservationPolicySnapshot policy =
                new KnowledgeObservationPolicySnapshot().Normalized();
            KnowledgeOpinionBandThresholds bands = new KnowledgeOpinionBandThresholds();
            string epoch = M6Epoch("Pawn_A");

            KnowledgeOpinionPlan baseline = KnowledgeRelationPolicy.PlanDirectedOpinion(
                null, null, M6Opinion("Pawn_A", epoch, "Pawn_B", 0, 100), bands, policy);
            AssertTrue("m6.opinion.baselineSilent", baseline.silentBaseline);
            AssertTrue("m6.opinion.baselineNoEpisode", baseline.openEpisode == null);

            KnowledgeOpinionPlan drift = KnowledgeRelationPolicy.PlanDirectedOpinion(
                baseline.replacement, null,
                M6Opinion("Pawn_A", epoch, "Pawn_B", 3, 200), bands, policy);
            AssertTrue("m6.opinion.pointDriftNoQualification", !drift.qualifiedForFutureCapture);
            AssertTrue("m6.opinion.pointDriftOpensNoCandidate", drift.openEpisode == null);
            KnowledgeOpinionPlan cumulative = KnowledgeRelationPolicy.PlanDirectedOpinion(
                drift.replacement, drift.openEpisode,
                M6Opinion("Pawn_A", epoch, "Pawn_B", 21, 300), bands, policy);
            AssertTrue("m6.opinion.sameBandCumulativeDoesNotQualify",
                !cumulative.qualifiedForFutureCapture);
            AssertTrue("m6.opinion.sameBandCumulativeOpensNoCandidate",
                cumulative.openEpisode == null);

            KnowledgeOpinionPlan bandBaseline = KnowledgeRelationPolicy.PlanDirectedOpinion(
                null, null, M6Opinion("Pawn_A", epoch, "Pawn_C", 20, 1000), bands, policy);
            KnowledgeOpinionPlan crossing = KnowledgeRelationPolicy.PlanDirectedOpinion(
                bandBaseline.replacement, null,
                M6Opinion("Pawn_A", epoch, "Pawn_C", 30, 1100), bands, policy);
            AssertTrue("m6.opinion.bandNotImmediate", !crossing.qualifiedForFutureCapture);
            KnowledgeOpinionPlan sustained = KnowledgeRelationPolicy.PlanDirectedOpinion(
                crossing.replacement, crossing.openEpisode,
                M6Opinion("Pawn_A", epoch, "Pawn_C", 30,
                    1100 + policy.opinionBandSustainTicks), bands, policy);
            AssertTrue("m6.opinion.bandSustained", sustained.qualifiedForFutureCapture);
            AssertEqual("m6.opinion.bandReason", "band_crossing",
                sustained.qualificationReasonToken);

            KnowledgeOpinionPlan reversalBaseline = KnowledgeRelationPolicy.PlanDirectedOpinion(
                null, null, M6Opinion("Pawn_A", epoch, "Pawn_D", 0, 2000), bands, policy);
            KnowledgeOpinionPlan rising = KnowledgeRelationPolicy.PlanDirectedOpinion(
                reversalBaseline.replacement, null,
                M6Opinion("Pawn_A", epoch, "Pawn_D", 10, 2100), bands, policy);
            KnowledgeOpinionPlan reversal = KnowledgeRelationPolicy.PlanDirectedOpinion(
                rising.replacement, rising.openEpisode,
                M6Opinion("Pawn_A", epoch, "Pawn_D", -5, 2200), bands, policy);
            AssertTrue("m6.opinion.sameBandReversalDoesNotQualify",
                !reversal.qualifiedForFutureCapture);
            AssertTrue("m6.opinion.sameBandReversalOpensNoCandidate",
                reversal.openEpisode == null);

            KnowledgeOpinionPlan largeJumpBaseline = KnowledgeRelationPolicy.PlanDirectedOpinion(
                null, null, M6Opinion("Pawn_A", epoch, "Pawn_J", 0, 2300), bands, policy);
            KnowledgeOpinionPlan largeJump = KnowledgeRelationPolicy.PlanDirectedOpinion(
                largeJumpBaseline.replacement, null,
                M6Opinion("Pawn_A", epoch, "Pawn_J", 70, 2400), bands, policy);
            AssertTrue("m6.opinion.largeBandJumpWaits",
                !largeJump.qualifiedForFutureCapture && largeJump.openEpisode != null);
            KnowledgeOpinionPlan largeJumpSustained = KnowledgeRelationPolicy.PlanDirectedOpinion(
                largeJump.replacement, largeJump.openEpisode,
                M6Opinion("Pawn_A", epoch, "Pawn_J", 70,
                    2400 + policy.opinionBandSustainTicks), bands, policy);
            AssertTrue("m6.opinion.largeBandJumpQualifiesAfterSustain",
                largeJumpSustained.qualifiedForFutureCapture);
            AssertEqual("m6.opinion.largeBandJumpReason", "band_crossing",
                largeJumpSustained.qualificationReasonToken);

            KnowledgeOpinionPlan inactivityBaseline = KnowledgeRelationPolicy.PlanDirectedOpinion(
                null, null, M6Opinion("Pawn_A", epoch, "Pawn_E", 20, 3000), bands, policy);
            KnowledgeOpinionPlan inactivityCandidate = KnowledgeRelationPolicy.PlanDirectedOpinion(
                inactivityBaseline.replacement, null,
                M6Opinion("Pawn_A", epoch, "Pawn_E", 30, 3100), bands, policy);
            KnowledgeOpinionPlan inactivityExpired = KnowledgeRelationPolicy.PlanDirectedOpinion(
                inactivityCandidate.replacement, inactivityCandidate.openEpisode,
                M6Opinion("Pawn_A", epoch, "Pawn_E", 31,
                    inactivityCandidate.openEpisode.lastObservedTick
                        + policy.opinionEpisodeInactivityTicks),
                bands,
                policy);
            AssertTrue("m6.opinion.inactivityCloses",
                inactivityExpired.openEpisode == null);
            AssertTrue("m6.opinion.inactivityNoQualification",
                !inactivityExpired.qualifiedForFutureCapture);

            KnowledgeOpinionPlan maximumBaseline = KnowledgeRelationPolicy.PlanDirectedOpinion(
                null, null, M6Opinion("Pawn_A", epoch, "Pawn_F", 20, 4000), bands, policy);
            KnowledgeOpinionPlan maximumCandidate = KnowledgeRelationPolicy.PlanDirectedOpinion(
                maximumBaseline.replacement, null,
                M6Opinion("Pawn_A", epoch, "Pawn_F", 30, 4100), bands, policy);
            KnowledgeOpinionEpisodeState maximumEpisode = M6EpisodeCopy(
                maximumCandidate.openEpisode);
            maximumEpisode.firstObservedTick = 4100;
            maximumEpisode.lastObservedTick = 4100
                + policy.opinionEpisodeMaximumTicks - 1;
            KnowledgeOpinionPlan maximumExpired = KnowledgeRelationPolicy.PlanDirectedOpinion(
                maximumCandidate.replacement, maximumEpisode,
                M6Opinion("Pawn_A", epoch, "Pawn_F", 31,
                    4100 + policy.opinionEpisodeMaximumTicks),
                bands,
                policy);
            AssertTrue("m6.opinion.maximumCloses", maximumExpired.openEpisode == null);
            AssertTrue("m6.opinion.maximumNoQualification",
                !maximumExpired.qualifiedForFutureCapture);

            KnowledgeOpinionPlan targetJitterBaseline =
                KnowledgeRelationPolicy.PlanDirectedOpinion(
                    null, null, M6Opinion("Pawn_A", epoch, "Pawn_G", 24, 5000), bands, policy);
            KnowledgeOpinionPlan targetJitterCrossing =
                KnowledgeRelationPolicy.PlanDirectedOpinion(
                    targetJitterBaseline.replacement, null,
                    M6Opinion("Pawn_A", epoch, "Pawn_G", 31, 5100), bands, policy);
            KnowledgeOpinionPlan targetJitter =
                KnowledgeRelationPolicy.PlanDirectedOpinion(
                    targetJitterCrossing.replacement, targetJitterCrossing.openEpisode,
                    M6Opinion("Pawn_A", epoch, "Pawn_G", 30, 5200), bands, policy);
            AssertTrue("m6.opinion.targetBandJitterStaysOpen",
                targetJitter.openEpisode != null);
            AssertTrue("m6.opinion.targetBandJitterNoEarlyQualification",
                !targetJitter.qualifiedForFutureCapture);
            AssertEqual("m6.opinion.targetBandJitterKeepsIdentity",
                targetJitterCrossing.openEpisode.episodeId,
                targetJitter.openEpisode.episodeId);
            AssertEqual("m6.opinion.targetBandJitterKeepsSustainStart",
                targetJitterCrossing.openEpisode.firstObservedTick,
                targetJitter.openEpisode.firstObservedTick);
            KnowledgeOpinionPlan targetJitterSustained =
                KnowledgeRelationPolicy.PlanDirectedOpinion(
                    targetJitter.replacement, targetJitter.openEpisode,
                    M6Opinion("Pawn_A", epoch, "Pawn_G", 30,
                        5100 + policy.opinionBandSustainTicks), bands, policy);
            AssertTrue("m6.opinion.targetBandJitterStillQualifies",
                targetJitterSustained.qualifiedForFutureCapture);

            KnowledgeOpinionPlan retargetBaseline = KnowledgeRelationPolicy.PlanDirectedOpinion(
                null, null, M6Opinion("Pawn_A", epoch, "Pawn_I", 24, 8000), bands, policy);
            KnowledgeOpinionPlan retargetFriendly = KnowledgeRelationPolicy.PlanDirectedOpinion(
                retargetBaseline.replacement, null,
                M6Opinion("Pawn_A", epoch, "Pawn_I", 30, 8100), bands, policy);
            long retargetAlmostTick = 8100 + policy.opinionBandSustainTicks - 1;
            KnowledgeOpinionPlan retargetAlmost = KnowledgeRelationPolicy.PlanDirectedOpinion(
                retargetFriendly.replacement, retargetFriendly.openEpisode,
                M6Opinion("Pawn_A", epoch, "Pawn_I", 31, retargetAlmostTick), bands, policy);
            AssertTrue("m6.opinion.firstTargetNotYetSustained",
                !retargetAlmost.qualifiedForFutureCapture);
            long devotedTargetTick = retargetAlmostTick + 1;
            KnowledgeOpinionPlan retargetDevoted = KnowledgeRelationPolicy.PlanDirectedOpinion(
                retargetAlmost.replacement, retargetAlmost.openEpisode,
                M6Opinion("Pawn_A", epoch, "Pawn_I",
                    bands.devoted + policy.opinionHysteresisPoints,
                    devotedTargetTick), bands, policy);
            AssertTrue("m6.opinion.changedTargetDoesNotReuseOldTimer",
                !retargetDevoted.qualifiedForFutureCapture);
            AssertEqual("m6.opinion.changedTargetRestartsSustain",
                devotedTargetTick, retargetDevoted.openEpisode.firstObservedTick);
            KnowledgeOpinionPlan retargetDevotedSustained =
                KnowledgeRelationPolicy.PlanDirectedOpinion(
                    retargetDevoted.replacement, retargetDevoted.openEpisode,
                    M6Opinion("Pawn_A", epoch, "Pawn_I",
                        bands.devoted + policy.opinionHysteresisPoints,
                        devotedTargetTick + policy.opinionBandSustainTicks), bands, policy);
            AssertTrue("m6.opinion.changedTargetQualifiesAfterOwnSustain",
                retargetDevotedSustained.qualifiedForFutureCapture);

            KnowledgeOpinionPlan downwardBaseline = KnowledgeRelationPolicy.PlanDirectedOpinion(
                null, null, M6Opinion("Pawn_A", epoch, "Pawn_H", 30, 6000), bands, policy);
            KnowledgeOpinionPlan downwardCrossing = KnowledgeRelationPolicy.PlanDirectedOpinion(
                downwardBaseline.replacement, null,
                M6Opinion("Pawn_A", epoch, "Pawn_H", 24, 6100), bands, policy);
            long downwardSustainTick = 6100 + policy.opinionBandSustainTicks;
            KnowledgeOpinionPlan downwardBoundary = KnowledgeRelationPolicy.PlanDirectedOpinion(
                downwardCrossing.replacement, downwardCrossing.openEpisode,
                M6Opinion("Pawn_A", epoch, "Pawn_H",
                    bands.friendly - policy.opinionHysteresisPoints,
                    downwardSustainTick),
                bands,
                policy);
            AssertTrue("m6.opinion.downwardBoundaryDoesNotQualify",
                !downwardBoundary.qualifiedForFutureCapture);
            AssertTrue("m6.opinion.downwardBoundaryRemainsOpen",
                downwardBoundary.openEpisode != null);
            KnowledgeOpinionPlan downwardBeyond = KnowledgeRelationPolicy.PlanDirectedOpinion(
                downwardBoundary.replacement, downwardBoundary.openEpisode,
                M6Opinion("Pawn_A", epoch, "Pawn_H",
                    bands.friendly - policy.opinionHysteresisPoints - 1,
                    downwardSustainTick + 1),
                bands,
                policy);
            AssertTrue("m6.opinion.downwardBeyondQualifies",
                downwardBeyond.qualifiedForFutureCapture);
            AssertEqual("m6.opinion.downwardBeyondReason", "band_crossing",
                downwardBeyond.qualificationReasonToken);

            KnowledgeOpinionObservation relationChange =
                M6Opinion("Pawn_A", epoch, "Pawn_B", 21, 400);
            relationChange.outboundRelationDefNames.Add("Spouse");
            KnowledgeOpinionPlan formal = KnowledgeRelationPolicy.PlanDirectedOpinion(
                cumulative.replacement, null, relationChange, bands, policy);
            AssertTrue("m6.opinion.formalCloses", formal.formalRelationChanged);
            AssertTrue("m6.opinion.formalNoEpisode", formal.openEpisode == null);

            KnowledgeOpinionObservation disabledObservation =
                M6Opinion("Pawn_A", epoch, "Pawn_B", 25, 500);
            disabledObservation.captureAllowed = false;
            KnowledgeOpinionPlan disabled = KnowledgeRelationPolicy.PlanDirectedOpinion(
                formal.replacement, null, disabledObservation, bands, policy);
            AssertTrue("m6.opinion.disabledSilent", disabled.silentBaseline);
            AssertTrue("m6.opinion.disabledNoEpisode", disabled.openEpisode == null);
            disabledObservation.captureAllowed = true;
            disabledObservation.captureInvalidationGeneration = 2;
            disabledObservation.observedTick = 600;
            KnowledgeOpinionPlan reenabled = KnowledgeRelationPolicy.PlanDirectedOpinion(
                disabled.replacement, null, disabledObservation, bands, policy);
            AssertTrue("m6.opinion.reenabledSilent", reenabled.silentBaseline);
            AssertTrue("m6.opinion.reenabledNoBacklog", reenabled.openEpisode == null);

            KnowledgeOpinionEpisodeState saturatedEpisode =
                M6EpisodeCopy(retargetDevoted.openEpisode);
            saturatedEpisode.episodeRevision = long.MaxValue - 1;
            KnowledgeOpinionPlan saturatedAdvance = KnowledgeRelationPolicy.PlanDirectedOpinion(
                retargetDevoted.replacement,
                saturatedEpisode,
                M6Opinion("Pawn_A", epoch, "Pawn_I",
                    bands.devoted + policy.opinionHysteresisPoints + 1,
                    devotedTargetTick + 1),
                bands,
                policy);
            AssertTrue("m6.opinion.saturatedEpisodeRemoved",
                saturatedAdvance.openEpisode == null);
            AssertEqual("m6.opinion.saturatedAwarenessMarker",
                KnowledgeObservationTokens.TrackingCapacityUntracked,
                saturatedAdvance.replacement.trackingStateToken);
            AssertEqual("m6.opinion.saturatedAwarenessRevision",
                long.MaxValue, saturatedAdvance.replacement.snapshotRevision);
            AssertEqual("m6.opinion.saturatedAwarenessNoFacts",
                0, saturatedAdvance.replacement.stateFacts.Count);
            AssertTrue("m6.opinion.saturatedSilent", saturatedAdvance.silentBaseline);
            AssertTrue("m6.opinion.saturatedNoQualification",
                !saturatedAdvance.qualifiedForFutureCapture);
            KnowledgeOpinionPlan saturatedNoOp = KnowledgeRelationPolicy.PlanDirectedOpinion(
                saturatedAdvance.replacement,
                null,
                M6Opinion("Pawn_A", epoch, "Pawn_I",
                    bands.devoted + policy.opinionHysteresisPoints + 2,
                    devotedTargetTick + 2),
                bands,
                policy);
            AssertTrue("m6.opinion.saturatedNoSavedMutation",
                !saturatedNoOp.savedMutationRequired);
            AssertTrue("m6.opinion.saturatedNoNewEpisode",
                saturatedNoOp.openEpisode == null);
        }

        private static void TestM6FactionOwnerAwarenessSeparation()
        {
            AssertEqual("m6.faction.connectionLiveCurrent",
                KnowledgeObservationTokens.ConnectionCurrent,
                KnowledgeRelationPolicy.OwnerFactionConnectionKind(true));
            AssertEqual("m6.faction.connectionStaleFormer",
                KnowledgeObservationTokens.ConnectionRecentFormer,
                KnowledgeRelationPolicy.OwnerFactionConnectionKind(false));
            AssertEqual("m6.faction.familyCannotDowngradeCurrent",
                KnowledgeObservationTokens.ConnectionCurrent,
                KnowledgeRelationPolicy.PreferPersonalFactionConnection(
                    KnowledgeObservationTokens.ConnectionCurrent,
                    KnowledgeObservationTokens.ConnectionFamily));
            AssertEqual("m6.faction.familyCannotDowngradeFormer",
                KnowledgeObservationTokens.ConnectionRecentFormer,
                KnowledgeRelationPolicy.PreferPersonalFactionConnection(
                    KnowledgeObservationTokens.ConnectionRecentFormer,
                    KnowledgeObservationTokens.ConnectionFamily));
            AssertEqual("m6.faction.familyFillsUnknown",
                KnowledgeObservationTokens.ConnectionFamily,
                KnowledgeRelationPolicy.PreferPersonalFactionConnection(
                    string.Empty,
                    KnowledgeObservationTokens.ConnectionFamily));
            AssertEqual("m6.faction.exactTransitionReplacesOld",
                KnowledgeObservationTokens.ConnectionRecentFormer,
                KnowledgeRelationPolicy.PreferPersonalFactionConnection(
                    KnowledgeObservationTokens.ConnectionCurrent,
                    KnowledgeObservationTokens.ConnectionRecentFormer));

            long generation;
            AssertTrue("m6.faction.allocate",
                KnowledgeRelationPolicy.TryAllocateFactionGeneration(
                    0, new long[0], out generation));
            AssertEqual("m6.faction.allocateOne", 1L, generation);
            AssertTrue("m6.faction.allocateCollisionRefused",
                !KnowledgeRelationPolicy.TryAllocateFactionGeneration(
                    0, new[] { 1L }, out generation));
            AssertTrue("m6.faction.allocateSaturationRefused",
                !KnowledgeRelationPolicy.TryAllocateFactionGeneration(
                    long.MaxValue, new long[0], out generation));

            string firstSubject;
            string secondSubject;
            AssertTrue("m6.faction.subjectFirst", MemoryIdentityCodec.TryCreateFactionSubjectId(
                "Faction_17", 1, out firstSubject));
            AssertTrue("m6.faction.subjectSecond", MemoryIdentityCodec.TryCreateFactionSubjectId(
                "Faction_18", 1, out secondSubject));
            AssertTrue("m6.faction.equalLabelDefNoCollision", firstSubject != secondSubject);

            KnowledgeFactionObservation factionObservation = new KnowledgeFactionObservation
            {
                factionInstanceId = "Faction_17",
                allocatorGeneration = 1,
                factionDefName = "SameDef",
                frozenDisplayLabel = "Same label",
                goodwill = 10,
                relationKindToken = "Neutral",
                leaderPawnId = "Pawn_Leader",
                observedTick = 10
            };
            KnowledgeFactionPlan factionBaseline = KnowledgeRelationPolicy.PlanFactionSnapshot(
                null, factionObservation);
            AssertTrue("m6.faction.baselineValid", factionBaseline.valid);
            AssertTrue("m6.faction.baselineSilent", factionBaseline.silentBaseline);
            AssertTrue("m6.faction.trackedCanInferMissingRemoval",
                KnowledgeRelationPolicy.CanInferMissingFactionRemoval(
                    factionBaseline.replacement));
            factionObservation.relationKindToken = "FriendlyLabel";
            AssertTrue("m6.faction.nonRelationLabelRejected",
                !KnowledgeRelationPolicy.PlanFactionSnapshot(null, factionObservation).valid);
            factionObservation.relationKindToken = KnowledgeObservationTokens.FactionRelationNeutral;
            factionObservation.goodwill = 25;
            factionObservation.observedTick = 20;
            KnowledgeFactionPlan factionChanged = KnowledgeRelationPolicy.PlanFactionSnapshot(
                factionBaseline.replacement, factionObservation);
            AssertTrue("m6.faction.changeDetected", factionChanged.authoritativeStateChanged);
            AssertTrue("m6.faction.changeNotBaseline", !factionChanged.silentBaseline);
            factionChanged.replacement.snapshotRevision = long.MaxValue - 1;
            factionObservation.observedTick = 30;
            KnowledgeFactionPlan factionSaturated = KnowledgeRelationPolicy.PlanFactionSnapshot(
                factionChanged.replacement, factionObservation);
            AssertEqual("m6.faction.saturationMarker",
                KnowledgeObservationTokens.TrackingCapacityUntracked,
                factionSaturated.replacement.trackingStateToken);
            AssertTrue("m6.faction.markerCannotInferMissingRemoval",
                !KnowledgeRelationPolicy.CanInferMissingFactionRemoval(
                    factionSaturated.replacement));
            KnowledgeFactionState alreadyRemoved = M6FactionCopy(factionBaseline.replacement);
            alreadyRemoved.removed = true;
            AssertTrue("m6.faction.removedCannotInferRemovalAgain",
                !KnowledgeRelationPolicy.CanInferMissingFactionRemoval(alreadyRemoved));
            long factionSaturatedTick = factionSaturated.replacement.observedTick;
            factionObservation.observedTick = 40;
            KnowledgeFactionPlan factionTerminal = KnowledgeRelationPolicy.PlanFactionSnapshot(
                factionSaturated.replacement, factionObservation);
            AssertTrue("m6.faction.terminalNoSavedMutation",
                !factionTerminal.savedMutationRequired);
            AssertEqual("m6.faction.terminalTickStable",
                factionSaturatedTick, factionTerminal.replacement.observedTick);

            string epoch = M6Epoch("Pawn_A");
            KnowledgeAwarenessPlan owner = KnowledgeRelationPolicy.PlanCurrentTruth(
                null,
                new KnowledgeCurrentTruthObservation
                {
                    ownerPawnId = "Pawn_A",
                    ownerEpochToken = epoch,
                    scopeKindToken = KnowledgeObservationTokens.ScopeFaction,
                    subjectKind = KnowledgeObservationTokens.SubjectFaction,
                    subjectId = firstSubject,
                    factStreamToken = KnowledgeObservationTokens.StreamFactionConnection,
                    captureInvalidationGeneration = 1,
                    knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceDirect,
                    stateFacts = new List<KnowledgeStateFact>
                    {
                        new KnowledgeStateFact
                        {
                            key = KnowledgeObservationTokens.FactConnectionKind,
                            value = KnowledgeObservationTokens.ConnectionFamily
                        }
                    },
                    observedTick = 100,
                    captureAllowed = true
                },
                new KnowledgeObservationPolicySnapshot());
            AssertTrue("m6.faction.ownerValid", owner.valid);
            AssertEqual("m6.faction.ownerOneConnectionFact", 1,
                owner.replacement.stateFacts.Count);
            AssertTrue("m6.faction.ownerNoGoodwillDuplication",
                owner.replacement.stateFacts.All(f =>
                    f.key.IndexOf("goodwill", StringComparison.OrdinalIgnoreCase) < 0
                    && f.key.IndexOf("relation", StringComparison.OrdinalIgnoreCase) < 0));

            string otherOwnerId;
            AssertTrue("m6.faction.otherOwnerIdentity",
                KnowledgeRelationPolicy.TryCreateAwarenessId(
                    "Pawn_B", epoch, KnowledgeObservationTokens.ScopeFaction,
                    KnowledgeObservationTokens.SubjectFaction, firstSubject,
                    KnowledgeObservationTokens.StreamFactionConnection, out otherOwnerId));
            AssertTrue("m6.faction.ownerScoped", owner.replacement.snapshotId != otherOwnerId);
        }

        private static KnowledgeAwarenessState M6AwarenessCopy(KnowledgeAwarenessState source)
        {
            return new KnowledgeAwarenessState
            {
                snapshotId = source.snapshotId,
                scopeKindToken = source.scopeKindToken,
                subjectKind = source.subjectKind,
                subjectId = source.subjectId,
                factStreamToken = source.factStreamToken,
                captureInvalidationGeneration = source.captureInvalidationGeneration,
                knownnessEvidenceToken = source.knownnessEvidenceToken,
                stateFacts = source.stateFacts.Select(f => new KnowledgeStateFact
                {
                    key = f.key,
                    value = f.value
                }).ToList(),
                firstObservedTick = source.firstObservedTick,
                lastObservedTick = source.lastObservedTick,
                lastSourceOccurrenceId = source.lastSourceOccurrenceId,
                trackingStateToken = source.trackingStateToken,
                snapshotRevision = source.snapshotRevision
            };
        }

        private static List<KnowledgeStateFact> M6OpinionFacts(int opinion)
        {
            return new List<KnowledgeStateFact>
            {
                new KnowledgeStateFact
                {
                    key = KnowledgeObservationTokens.FactOpinionValue,
                    value = opinion.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                new KnowledgeStateFact
                {
                    key = KnowledgeObservationTokens.FactOpinionBand,
                    value = KnowledgeRelationPolicy.OpinionBandToken(
                        opinion, new KnowledgeOpinionBandThresholds())
                }
            };
        }

        private static KnowledgeOpinionEpisodeState M6EpisodeCopy(
            KnowledgeOpinionEpisodeState source)
        {
            return new KnowledgeOpinionEpisodeState
            {
                episodeId = source.episodeId,
                captureRuleId = source.captureRuleId,
                scopeKindToken = source.scopeKindToken,
                factStreamToken = source.factStreamToken,
                category = source.category,
                captureInvalidationGeneration = source.captureInvalidationGeneration,
                episodeKindToken = source.episodeKindToken,
                subjectKind = source.subjectKind,
                subjectId = source.subjectId,
                pairOrStreamKey = source.pairOrStreamKey,
                directionToken = source.directionToken,
                baselineFacts = source.baselineFacts.Select(f => new KnowledgeStateFact
                {
                    key = f.key,
                    value = f.value
                }).ToList(),
                currentFacts = source.currentFacts.Select(f => new KnowledgeStateFact
                {
                    key = f.key,
                    value = f.value
                }).ToList(),
                firstObservedTick = source.firstObservedTick,
                lastObservedTick = source.lastObservedTick,
                lastSourceOccurrenceId = source.lastSourceOccurrenceId,
                episodeRevision = source.episodeRevision
            };
        }

        private static KnowledgeFactionState M6FactionCopy(KnowledgeFactionState source)
        {
            return new KnowledgeFactionState
            {
                factionInstanceId = source.factionInstanceId,
                allocatorGeneration = source.allocatorGeneration,
                factionDefName = source.factionDefName,
                frozenDisplayLabel = source.frozenDisplayLabel,
                goodwill = source.goodwill,
                relationKindToken = source.relationKindToken,
                leaderPawnId = source.leaderPawnId,
                defeated = source.defeated,
                removed = source.removed,
                observedTick = source.observedTick,
                trackingStateToken = source.trackingStateToken,
                snapshotRevision = source.snapshotRevision
            };
        }

        private static KnowledgeOpinionObservation M6Opinion(
            string owner,
            string epoch,
            string subject,
            int opinion,
            long tick)
        {
            return new KnowledgeOpinionObservation
            {
                ownerPawnId = owner,
                ownerEpochToken = epoch,
                subjectPawnId = subject,
                opinion = opinion,
                captureInvalidationGeneration = 1,
                observedTick = tick,
                captureAllowed = true
            };
        }

        private static string M6Epoch(string owner)
        {
            MemoryEpochAllocationPlan plan = MemoryIdentityCodec.PlanEpochAllocation(
                new MemoryEpochAllocationRequest
                {
                    ownerPawnId = owner,
                    lastIssuedSequence = 0,
                    fallbackChain = string.Empty,
                    liveEpochCarriers = new List<string>(),
                    isTargetBrainwipe = false
                });
            AssertTrue("m6.epoch." + owner, plan.canMutate);
            return plan.epochToken;
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

        private static void TestAnnotationLocalizedTextTerms()
        {
            CultureTopicRule mechanoids = Topic("mechanoids", 10);
            mechanoids.triggerTextTerms.Add("mechanoid*");
            CultureProfile profile = Profile("Astropolitan",
                "mechanoids", "hardware, not spirits");
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();

            CultureAnnotationPlan plan = CultureAnnotationPlanner.Plan(
                new List<AnnotationFieldView>
                {
                    Field(0, "PovText", "They talked about mechanoids.")
                },
                "Chitchat", new List<CultureTopicRule> { mechanoids },
                profile, null, policy);
            AssertEqual("detect.text.userCase.count", 1, plan.entries.Count);
            AssertEqual("detect.text.userCase.topic", "mechanoids", plan.matchedTopics[0]);
            AssertEqual("detect.text.userCase.field", 0, plan.entries[0].fieldIndex);

            // Prefix terms cover grammatical inflection without falling back to substring matching.
            CultureTopicRule russianMechanoids = Topic("mechanoids", 10);
            russianMechanoids.triggerTextTerms.Add("механоид*");
            plan = CultureAnnotationPlanner.Plan(
                new List<AnnotationFieldView>
                {
                    Field(0, "PovText", "Они говорили о механоидах.")
                },
                "Chitchat", new List<CultureTopicRule> { russianMechanoids },
                profile, null, policy);
            AssertEqual("detect.text.russianInflection", 1, plan.entries.Count);

            // Each word in a phrase can opt into suffix matching.
            CultureTopicRule archotech = Topic("archotech", 20);
            archotech.triggerTextTerms.Add("ancient* complex*");
            CultureProfile archotechProfile = Profile("Astropolitan",
                "archotech", "engineering beyond human scale");
            plan = CultureAnnotationPlanner.Plan(
                new List<AnnotationFieldView>
                {
                    Field(2, "NeutralText", "They searched the ancient complexes.")
                },
                "Chitchat", new List<CultureTopicRule> { archotech },
                archotechProfile, null, policy);
            AssertEqual("detect.text.phrasePrefix", 1, plan.entries.Count);

            // Whole-word matching prevents the old substring-classifier failure mode.
            CultureTopicRule empire = Topic("empire", 30);
            empire.triggerTextTerms.Add("empire");
            CultureProfile empireProfile = Profile("Astropolitan",
                "empire", "one polity among many");
            plan = CultureAnnotationPlanner.Plan(
                new List<AnnotationFieldView>
                {
                    Field(0, "PovText", "A vampire bat crossed the yard.")
                },
                "Chitchat", new List<CultureTopicRule> { empire },
                empireProfile, null, policy);
            AssertEqual("detect.text.wordBoundary", 0, plan.entries.Count);

            AssertEqual("detect.text.invalidBareWildcard", false,
                CultureTextTermMatcher.IsValidPattern("*"));
            AssertEqual("detect.text.invalidShortPrefix", false,
                CultureTextTermMatcher.IsValidPattern("a*"));
            AssertEqual("detect.text.validUnicodePrefix", true,
                CultureTextTermMatcher.IsValidPattern("механоид*"));
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

            // Past-memory text, already-generated narrative/belief layers, and prior entries are
            // structurally unscannable: their sources are absent from the allowlist, so composition
            // cannot feed generated interpretation back into a second culture annotation.
            topic.triggerTextTerms.Add("xenohuman*");
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            List<AnnotationFieldView> excluded = new List<AnnotationFieldView>
            {
                Field(0, "MemoryContext", "- (day) xenotype=Hussar changed"),
                Field(1, "BeliefContext", "xenotype=Hussar is current doctrine"),
                Field(2, "NarrativeContext", "xenotype=Hussar is a selected shared lens"),
                Field(3, "EntryText", "yesterday xenotype=Hussar"),
                Field(4, "HiddenInitiatorEntry", "xenotype=Hussar"),
                Field(5, "MemoryContext", "they remembered the xenohumans")
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
            // The planner runs once, pre-annotation. The parenthetical format itself is inert:
            // this topic has one structured marker and no authored lexical terms that occur here.
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

        private static void TestRecallV2ConsumerAndExactEligibilityContract()
        {
            List<MemoryRecallConsumerContract> consumers = MemoryRecallConsumerRegistry.All();
            AssertEqual("recallV2.consumer.count", 7, consumers.Count);
            foreach (MemoryRecallConsumerContract consumer in consumers)
            {
                AssertTrue("recallV2.consumer.common." + consumer.consumerId,
                    consumer.appliesCommonExclusionContract
                    && consumer.requiresOwnerMatch
                    && consumer.requiresEpochMatch
                    && consumer.requiresCategoryEnabled
                    && consumer.honorsSuppression);
                AssertEqual("recallV2.consumer.compact." + consumer.consumerId,
                    0, consumer.compactMaximumLines);
                AssertEqual("recallV2.consumer.off." + consumer.consumerId,
                    0, consumer.offMaximumLines);
            }

            MemoryRecallQueryV2 query = RecallQuery("Owner_A", MemoryRecallWritingFormats.Full);
            List<ImportantMemoryDraft> typedDrafts = new List<ImportantMemoryDraft>
            {
                new ImportantMemoryDraft
                {
                    factual = new FactualMemoryDraft
                    {
                        ownerPawnId = "Owner_A",
                        routeReliable = true,
                        subjectKind = MemoryContractTokens.SubjectStream,
                        subjectId = MemoryContractTokens.StreamBodyHistory,
                        primarySubject = new FactualMemorySubjectDraft
                        {
                            subjectKind = MemoryContractTokens.SubjectPawn,
                            subjectId = "Pawn_ExactSubject"
                        },
                        secondarySubjects = new List<FactualMemorySubjectDraft>
                        {
                            new FactualMemorySubjectDraft
                            {
                                subjectKind = MemoryContractTokens.SubjectPawn,
                                subjectId = "Pawn_ExactSecondary"
                            }
                        }
                    }
                },
                new ImportantMemoryDraft
                {
                    factual = new FactualMemoryDraft
                    {
                        ownerPawnId = "Owner_Other",
                        routeReliable = true,
                        subjectKind = MemoryContractTokens.SubjectPawn,
                        subjectId = "Pawn_MustNotLeak"
                    }
                }
            };
            List<MemoryRecallRouteIdentity> typedRoutes =
                new List<MemoryRecallRouteIdentity>();
            AssertEqual("recallV2.query.typedDraftRouteCount", 3,
                ImportantMemorySelector.AddFactualDraftRoutes(
                    typedRoutes, typedDrafts, "Owner_A"));
            AssertTrue("recallV2.query.typedDraftRoutesExact",
                typedRoutes.Any(row => row.subjectKind == MemoryContractTokens.SubjectStream
                        && row.subjectId == MemoryContractTokens.StreamBodyHistory)
                    && typedRoutes.Any(row => row.subjectKind == MemoryContractTokens.SubjectPawn
                        && row.subjectId == "Pawn_ExactSubject")
                    && typedRoutes.Any(row => row.subjectId == "Pawn_ExactSecondary")
                    && typedRoutes.All(row => row.subjectId != "Pawn_MustNotLeak"));
            AssertTrue("recallV2.query.blankOrInvalidTypedRoutesRefused",
                !ImportantMemorySelector.TryAddExactRoute(
                    typedRoutes, MemoryContractTokens.SubjectPawn, string.Empty)
                && !ImportantMemorySelector.TryAddExactRoute(
                    typedRoutes, "legacy-guessed-kind", "part:Arm"));
            string recallAdapter = File.ReadAllText(Path.Combine(
                RepoRoot(), "Source", "Core", "DiaryGameComponent.MemoryRecallV2.cs"));
            AssertTrue("recallV2.query.legacySubjectKeysNeverGuessedIntoTypes",
                recallAdapter.IndexOf(
                    "legacyQuery.subjectKeys.Count", StringComparison.Ordinal) < 0);
            MemoryRecallQueryV2 reflectionQuery = RecallQuery(
                "Owner_A", MemoryRecallWritingFormats.Full);
            reflectionQuery.consumerId = MemoryRecallConsumerRegistry.ExistingReflection;
            MemoryRecallCandidateSnapshot reflectionCandidate = RecallCandidate(
                "reflection-common", "Owner_A", "source-reflection-common", 9);
            reflectionCandidate.currentStateApplicable = true;
            reflectionCandidate.currentStateCanRender = true;
            reflectionCandidate.currentStateText = "current truth";
            reflectionCandidate.currentStateSourceId =
                OrdinalSegmentCodec.Segment("awareness")
                + OrdinalSegmentCodec.Segment("reflection-common");
            AssertTrue("recallV2.reflection.commonStateEligible",
                ImportantMemorySelector.IsCandidateStateEligibleForConsumer(
                    reflectionQuery, reflectionQuery.consumerId, reflectionCandidate));
            reflectionCandidate.ttlEligible = false;
            AssertTrue("recallV2.reflection.expiredRejectedByCommonContract",
                !ImportantMemorySelector.IsCandidateStateEligibleForConsumer(
                    reflectionQuery, reflectionQuery.consumerId, reflectionCandidate));
            reflectionCandidate.ttlEligible = true;
            reflectionCandidate.currentStateContradictsHistorical = true;
            reflectionCandidate.currentStateCanRender = false;
            AssertTrue("recallV2.reflection.staleTruthRejectedByCommonContract",
                !ImportantMemorySelector.IsCandidateStateEligibleForConsumer(
                    reflectionQuery, reflectionQuery.consumerId, reflectionCandidate));
            string reflectionAdapter = File.ReadAllText(Path.Combine(
                RepoRoot(), "Source", "Core", "DiaryGameComponent.MemorySummaryWording.cs"));
            AssertTrue("recallV2.reflection.runtimeUsesCommonCandidateContract",
                reflectionAdapter.IndexOf(
                    "ImportantMemorySelector.IsCandidateStateEligibleForConsumer(",
                    StringComparison.Ordinal) >= 0);
            MemoryRecallCandidateSnapshot exact = RecallCandidate("exact", "Owner_A", "source-exact", 10);
            MemoryRecallCandidateSnapshot topicOnly = RecallCandidate("topic", "Owner_A", "source-topic", 20);
            topicOnly.exactRoutes[0].subjectId = "Pawn_Other";
            topicOnly.exactRoutes[0].routeKey = "route-other";
            topicOnly.requiredStructuralGuards.Clear();
            topicOnly.structuralGuardStates.Clear();
            AddSubjectGuard(topicOnly, "Pawn_Other");
            topicOnly.topicKeys.Add("body");
            query.topicKeys.Add("body");
            MemoryRecallSelectionResultV2 selected = ImportantMemorySelector.SelectV2(
                query, new List<MemoryRecallCandidateSnapshot> { topicOnly, exact });
            AssertEqual("recallV2.topic.never-door", 1, selected.selected.Count);
            AssertEqual("recallV2.topic.exact-wins", "exact",
                selected.selected[0].candidate.recordId);
            AssertEqual("recallV2.topic.reject", MemoryRecallRejectReasons.NoExactRoute,
                selected.report.First(row => row.recordId == "topic").rejectReason);

            MemoryRecallQueryV2 selfRouteQuery = RecallQuery(
                "Owner_A", MemoryRecallWritingFormats.Full);
            selfRouteQuery.exactRoutes.Clear();
            selfRouteQuery.exactRoutes.Add(RecallRoute("Owner_A"));
            MemoryRecallCandidateSnapshot selfRoute = RecallCandidate(
                "self-route", "Owner_A", "source-self-route", 25);
            selfRoute.exactRoutes.Clear();
            selfRoute.requiredStructuralGuards.Clear();
            selfRoute.structuralGuardStates.Clear();
            selfRoute.exactRoutes.Add(RecallRoute("Owner_A"));
            AddSubjectGuard(selfRoute, "Owner_A");
            selected = ImportantMemorySelector.SelectV2(
                selfRouteQuery, new List<MemoryRecallCandidateSnapshot> { selfRoute });
            AssertEqual("recallV2.owner-self-route-never-door",
                MemoryRecallRejectReasons.NoExactRoute,
                selected.report[0].rejectReason);

            MemoryRecallCandidateSnapshot wrongOwner = RecallCandidate(
                "wrong-owner", "Owner_B", "source-owner", 30);
            MemoryRecallCandidateSnapshot wrongEpoch = RecallCandidate(
                "wrong-epoch", "Owner_A", "source-epoch", 31);
            wrongEpoch.ownerEpochToken = "epoch-2";
            wrongEpoch.recordGuard.ownerEpochToken = "epoch-2";
            wrongEpoch.structuralGuardStates[0].ownerEpochToken = "epoch-2";
            MemoryRecallCandidateSnapshot suppressed = RecallCandidate(
                "suppressed", "Owner_A", "source-suppressed", 32);
            suppressed.suppressed = true;
            MemoryRecallCandidateSnapshot self = RecallCandidate(
                "self", "Owner_A", query.currentSourceOccurrenceId, 33);
            MemoryRecallCandidateSnapshot ancestor = RecallCandidate(
                "ancestor", "Owner_A", "source-ancestor", 34);
            ancestor.sourceEventId = "event-ancestor";
            query.excludedSourceEventIds.Add("event-ancestor");
            selected = ImportantMemorySelector.SelectV2(query,
                new List<MemoryRecallCandidateSnapshot>
                {
                    wrongOwner, wrongEpoch, suppressed, self, ancestor
                });
            AssertEqual("recallV2.exclusions.none", 0, selected.selected.Count);
            AssertEqual("recallV2.owner.reject", MemoryRecallRejectReasons.OwnerMismatch,
                selected.report[0].rejectReason);
            AssertEqual("recallV2.epoch.reject", MemoryRecallRejectReasons.EpochMismatch,
                selected.report[1].rejectReason);
            AssertEqual("recallV2.suppress.reject", MemoryRecallRejectReasons.Suppressed,
                selected.report[2].rejectReason);
            AssertEqual("recallV2.self.reject", MemoryRecallRejectReasons.CurrentEvent,
                selected.report[3].rejectReason);
            AssertEqual("recallV2.ancestor.reject", MemoryRecallRejectReasons.CurrentEvent,
                selected.report[4].rejectReason);

            MemoryRecallCandidateSnapshot category = RecallCandidate(
                "category", "Owner_A", "source-category", 40);
            query.enabledCategories.Clear();
            query.enabledCategories.Add(MemoryContractTokens.CategoryFamily);
            selected = ImportantMemorySelector.SelectV2(
                query, new List<MemoryRecallCandidateSnapshot> { category });
            AssertEqual("recallV2.category.reject", MemoryRecallRejectReasons.CategoryDisabled,
                selected.report[0].rejectReason);

            query.enabledCategories.Add(MemoryContractTokens.CategoryPersonal);
            MemoryRecallCandidateSnapshot oldThreadRow = RecallCandidate(
                "old-thread", "Owner_A", "source-thread", 41);
            MakeThreadCandidate(oldThreadRow, "root-1", "chapter-1");
            oldThreadRow.isCurrentThreadProjection = false;
            selected = ImportantMemorySelector.SelectV2(
                query, new List<MemoryRecallCandidateSnapshot> { oldThreadRow });
            AssertEqual("recallV2.thread-current-only",
                MemoryRecallRejectReasons.InvalidThreadProjection,
                selected.report[0].rejectReason);

            MemoryRecallQueryV2 incompleteExclusionQuery = RecallQuery(
                "Owner_A", MemoryRecallWritingFormats.Full);
            incompleteExclusionQuery.currentSourceOccurrenceId = string.Empty;
            selected = ImportantMemorySelector.SelectV2(
                incompleteExclusionQuery,
                new List<MemoryRecallCandidateSnapshot>
                {
                    RecallCandidate("self-contract", "Owner_A", "source-current", 44)
                });
            AssertEqual("recallV2.self-exclusion-contract-required",
                MemoryRecallRejectReasons.InvalidQuery,
                selected.report[0].rejectReason);

            MemoryRecallQueryV2 subjectlessQuery = RecallQuery(
                "Owner_A", MemoryRecallWritingFormats.Full);
            MemoryRecallCandidateSnapshot subjectless = RecallCandidate(
                "subjectless", "Owner_A", "source-subjectless", 45);
            subjectless.exactRoutes[0].subjectKind = string.Empty;
            subjectless.exactRoutes[0].subjectId = string.Empty;
            selected = ImportantMemorySelector.SelectV2(
                subjectlessQuery,
                new List<MemoryRecallCandidateSnapshot> { subjectless });
            AssertEqual("recallV2.subject-route-requires-subject",
                MemoryRecallRejectReasons.MissingOrCorrupt,
                selected.report[0].rejectReason);

            // One detached owner snapshot must never contain two meanings for the same record ID.
            // Treat both aliases as corrupt rather than letting rank/order choose a winner.
            MemoryRecallCandidateSnapshot duplicateRecordFirst = RecallCandidate(
                "duplicate-record", "Owner_A", "source-duplicate-first", 47);
            MemoryRecallCandidateSnapshot duplicateRecordSecond = RecallCandidate(
                "duplicate-record", "Owner_A", "source-duplicate-second", 46);
            selected = ImportantMemorySelector.SelectV2(
                RecallQuery("Owner_A", MemoryRecallWritingFormats.Full),
                new List<MemoryRecallCandidateSnapshot>
                {
                    duplicateRecordFirst,
                    duplicateRecordSecond
                });
            AssertEqual("recallV2.identity.duplicate-record-selects-none", 0,
                selected.selected.Count);
            AssertTrue("recallV2.identity.duplicate-records-fail-closed",
                selected.report.All(row =>
                    row.rejectReason == MemoryRecallRejectReasons.MissingOrCorrupt));

            MemoryRecallQueryV2 malformedRouteQuery = RecallQuery(
                "Owner_A", MemoryRecallWritingFormats.Full);
            malformedRouteQuery.exactRoutes.Add(new MemoryRecallRouteIdentity
            {
                routeKind = "future-route",
                routeKey = "future-route-key"
            });
            selected = ImportantMemorySelector.SelectV2(
                malformedRouteQuery,
                new List<MemoryRecallCandidateSnapshot>
                {
                    RecallCandidate("masked-query-route", "Owner_A", "source-masked-query", 48)
                });
            AssertEqual("recallV2.route.malformed-query-not-masked",
                MemoryRecallRejectReasons.InvalidQuery,
                selected.report[0].rejectReason);

            MemoryRecallCandidateSnapshot duplicateRouteCandidate = RecallCandidate(
                "duplicate-route", "Owner_A", "source-duplicate-route", 49);
            duplicateRouteCandidate.exactRoutes.Add(RecallRoute("Pawn_B"));
            selected = ImportantMemorySelector.SelectV2(
                RecallQuery("Owner_A", MemoryRecallWritingFormats.Full),
                new List<MemoryRecallCandidateSnapshot> { duplicateRouteCandidate });
            AssertEqual("recallV2.route.duplicate-candidate-not-masked",
                MemoryRecallRejectReasons.MissingOrCorrupt,
                selected.report[0].rejectReason);
        }

        private static void TestRecallV2RepetitionBoundariesAndGuardCompleteness()
        {
            MemoryRepetitionPolicySnapshot policy = RecallRepetitionPolicy();
            MemoryRepetitionGuardState record = GuardState(
                "epoch-1",
                MemoryRepetitionGuardKinds.Record,
                MemoryRepetitionGuardPolicy.RecordKey("record-1"),
                lastTick: 300000,
                lastEntry: 7,
                count: 1);
            List<MemoryRepetitionGuardState> guards = new List<MemoryRepetitionGuardState>
            {
                GuardState("epoch-1", MemoryRepetitionGuardKinds.Root,
                    MemoryRepetitionGuardPolicy.RootKey("root-1"), 1, 7, 1),
                GuardState("epoch-1", MemoryRepetitionGuardKinds.Subject,
                    MemoryRepetitionGuardPolicy.SubjectKey("pawn", "Pawn_B"), 1, 7, 1),
                GuardState("epoch-1", MemoryRepetitionGuardKinds.Pair,
                    MemoryRepetitionGuardPolicy.PairKey("Owner_A", "Pawn_B"), 1, 7, 1),
                GuardState("epoch-1", MemoryRepetitionGuardKinds.Novelty,
                    MemoryRepetitionGuardPolicy.NoveltyKey("root-1", "chapter-1"), 1, 7, 1)
            };
            MemoryRepetitionGuardEvaluation evaluation = MemoryRepetitionGuardPolicy.Evaluate(
                "epoch-1", record, guards, policy);
            AssertTrue("recallV2.guard.exact-boundaries-pass", evaluation.passes);
            AssertEqual("recallV2.guard.all-five", 5, evaluation.guardEntries.Count);
            AssertEqual("recallV2.guard.pair-order-invariant",
                MemoryRepetitionGuardPolicy.PairKey("Owner_A", "Pawn_B"),
                MemoryRepetitionGuardPolicy.PairKey("Pawn_B", "Owner_A"));
            string parsedSubjectKind;
            string parsedSubjectId;
            AssertTrue("recallV2.guard.subject-key-parse",
                MemoryRepetitionGuardPolicy.TryParseSubjectKey(
                    guards[1].guardKey, out parsedSubjectKind, out parsedSubjectId));
            AssertEqual("recallV2.guard.subject-key-kind", "pawn", parsedSubjectKind);
            AssertEqual("recallV2.guard.subject-key-id", "Pawn_B", parsedSubjectId);
            AssertTrue("recallV2.guard.subject-key-trailing-rejected",
                !MemoryRepetitionGuardPolicy.TryParseSubjectKey(
                    guards[1].guardKey + "1:x", out parsedSubjectKind, out parsedSubjectId));
            string parsedFirstEndpoint;
            string parsedSecondEndpoint;
            AssertTrue("recallV2.guard.pair-key-parse",
                MemoryRepetitionGuardPolicy.TryParsePairKey(
                    guards[2].guardKey, out parsedFirstEndpoint, out parsedSecondEndpoint));
            AssertEqual("recallV2.guard.pair-key-first", "Owner_A", parsedFirstEndpoint);
            AssertEqual("recallV2.guard.pair-key-second", "Pawn_B", parsedSecondEndpoint);
            string firstFaction;
            string secondFaction;
            AssertTrue("recallV2.guard.pair-faction-first",
                MemoryIdentityCodec.TryCreateFactionSubjectId(
                    "Faction_A", 71, out firstFaction));
            AssertTrue("recallV2.guard.pair-faction-second",
                MemoryIdentityCodec.TryCreateFactionSubjectId(
                    "Faction_B", 72, out secondFaction));
            string factionPair = MemoryRepetitionGuardPolicy.PairKey(
                firstFaction, secondFaction);
            AssertTrue("recallV2.guard.pair-faction-parse",
                MemoryRepetitionGuardPolicy.TryParsePairKey(
                    factionPair, out parsedFirstEndpoint, out parsedSecondEndpoint));
            AssertTrue("recallV2.guard.pair-faction-both-preserved",
                (parsedFirstEndpoint == firstFaction && parsedSecondEndpoint == secondFaction)
                    || (parsedFirstEndpoint == secondFaction
                        && parsedSecondEndpoint == firstFaction));

            record.lastAutomaticIncludedTick = 300001;
            evaluation = MemoryRepetitionGuardPolicy.Evaluate("epoch-1", record, guards, policy);
            AssertEqual("recallV2.guard.time-before-boundary",
                MemoryRepetitionRejectReasons.TimeDistance, evaluation.rejectReason);
            record.lastAutomaticIncludedTick = 300000;
            record.lastAutomaticIncludedEntryOrdinal = 8;
            evaluation = MemoryRepetitionGuardPolicy.Evaluate("epoch-1", record, guards, policy);
            AssertEqual("recallV2.guard.page-before-boundary",
                MemoryRepetitionRejectReasons.EntryDistance, evaluation.rejectReason);
            record.lastAutomaticIncludedEntryOrdinal = 7;
            guards[1].lastAutomaticIncludedEntryOrdinal = 8;
            evaluation = MemoryRepetitionGuardPolicy.Evaluate("epoch-1", record, guards, policy);
            AssertEqual("recallV2.guard.subject-before-boundary",
                MemoryRepetitionRejectReasons.EntryDistance, evaluation.rejectReason);
            guards[1].lastAutomaticIncludedEntryOrdinal = 7;
            guards[2].reserved = true;
            evaluation = MemoryRepetitionGuardPolicy.Evaluate("epoch-1", record, guards, policy);
            AssertEqual("recallV2.guard.reservation-hard",
                MemoryRepetitionRejectReasons.Reserved, evaluation.rejectReason);
            guards[2].reserved = false;
            record.automaticInclusionCount = long.MaxValue;
            evaluation = MemoryRepetitionGuardPolicy.Evaluate("epoch-1", record, guards, policy);
            AssertEqual("recallV2.guard.saturation-hard",
                MemoryRepetitionRejectReasons.Saturated, evaluation.rejectReason);
            record.automaticInclusionCount = 0;
            record.lastAutomaticIncludedTick = 0;
            record.lastAutomaticIncludedEntryOrdinal = 0;
            policy.completedDiaryEntryOrdinal = long.MaxValue;
            evaluation = MemoryRepetitionGuardPolicy.Evaluate("epoch-1", record, guards, policy);
            AssertEqual("recallV2.guard.owner-ordinal-saturation-hard",
                MemoryRepetitionRejectReasons.Saturated, evaluation.rejectReason);
            policy.completedDiaryEntryOrdinal = 10;
            policy.memoryReuseDays = 35792;
            evaluation = MemoryRepetitionGuardPolicy.Evaluate("epoch-1", record, guards, policy);
            AssertEqual("recallV2.guard.days-defensive-cap",
                MemoryRepetitionRejectReasons.InvalidPolicy, evaluation.rejectReason);
            policy.memoryReuseDays = 5;
            policy.memoryRevisitEntryCount = 1000001;
            evaluation = MemoryRepetitionGuardPolicy.Evaluate("epoch-1", record, guards, policy);
            AssertEqual("recallV2.guard.entries-defensive-cap",
                MemoryRepetitionRejectReasons.InvalidPolicy, evaluation.rejectReason);
            policy.memoryRevisitEntryCount = 3;

            List<MemoryRepetitionGuardState> malformedGuards =
                new List<MemoryRepetitionGuardState>
                {
                    GuardState("epoch-1", MemoryRepetitionGuardKinds.Root,
                        "not-length-prefixed", 0, 0, 0)
                };
            evaluation = MemoryRepetitionGuardPolicy.Evaluate(
                "epoch-1", record, malformedGuards, policy);
            AssertEqual("recallV2.guard.malformed-key-fails-closed",
                MemoryRepetitionRejectReasons.InvalidGuard, evaluation.rejectReason);

            List<MemoryRepetitionGuardState> futureGuards =
                new List<MemoryRepetitionGuardState>
                {
                    GuardState("epoch-1", "future_guard",
                        MemoryRepetitionGuardPolicy.RootKey("future-root"), 0, 0, 0)
                };
            evaluation = MemoryRepetitionGuardPolicy.Evaluate(
                "epoch-1", record, futureGuards, policy);
            AssertEqual("recallV2.guard.future-kind-fails-closed",
                MemoryRepetitionRejectReasons.InvalidGuard, evaluation.rejectReason);

            List<MemoryRepetitionGuardState> staleGuards =
                new List<MemoryRepetitionGuardState>
                {
                    GuardState("epoch-old", MemoryRepetitionGuardKinds.Root,
                        MemoryRepetitionGuardPolicy.RootKey("stale-root"), 0, 0, 0)
                };
            evaluation = MemoryRepetitionGuardPolicy.Evaluate(
                "epoch-1", record, staleGuards, policy);
            AssertEqual("recallV2.guard.stale-epoch-fails-closed",
                MemoryRepetitionRejectReasons.EpochMismatch, evaluation.rejectReason);

            MemoryRepetitionGuardState duplicateGuard = GuardState(
                "epoch-1",
                MemoryRepetitionGuardKinds.Subject,
                MemoryRepetitionGuardPolicy.SubjectKey(
                    MemoryContractTokens.SubjectPawn, "Pawn_Duplicate"),
                0, 0, 0);
            evaluation = MemoryRepetitionGuardPolicy.Evaluate(
                "epoch-1",
                record,
                new List<MemoryRepetitionGuardState> { duplicateGuard, duplicateGuard },
                policy);
            AssertEqual("recallV2.guard.duplicate-state-fails-closed",
                MemoryRepetitionRejectReasons.DuplicateGuard, evaluation.rejectReason);

            List<MemoryRepetitionGuardState> framedGuards =
                new List<MemoryRepetitionGuardState>
                {
                    GuardState("epoch-1", MemoryRepetitionGuardKinds.Subject,
                        MemoryRepetitionGuardPolicy.SubjectKey(
                            MemoryContractTokens.SubjectPawn, "Pawn\nA:1"), 0, 0, 0),
                    GuardState("epoch-1", MemoryRepetitionGuardKinds.Subject,
                        MemoryRepetitionGuardPolicy.SubjectKey(
                            MemoryContractTokens.SubjectPawn, "Pawn:A\n1"), 0, 0, 0)
                };
            evaluation = MemoryRepetitionGuardPolicy.Evaluate(
                "epoch-1", record, framedGuards, policy);
            AssertTrue("recallV2.guard.framed-identities-remain-distinct", evaluation.passes);
            AssertEqual("recallV2.guard.framed-identities-all-returned", 3,
                evaluation.guardEntries.Count);

            MemoryRecallQueryV2 query = RecallQuery("Owner_A", MemoryRecallWritingFormats.Full);
            MemoryRecallCandidateSnapshot bypass = RecallCandidate(
                "bypass", "Owner_A", "source-bypass", 50);
            bypass.structuralGuardStates.Clear();
            MemoryRecallSelectionResultV2 selected = ImportantMemorySelector.SelectV2(
                query, new List<MemoryRecallCandidateSnapshot> { bypass });
            AssertEqual("recallV2.guard.missing-state-no-bypass",
                MemoryRecallRejectReasons.GuardBypass,
                selected.report[0].rejectReason);

            MemoryRecallCandidateSnapshot extraState = RecallCandidate(
                "extra-state", "Owner_A", "source-extra-state", 51);
            extraState.structuralGuardStates.Add(GuardState(
                "epoch-1",
                MemoryRepetitionGuardKinds.Root,
                MemoryRepetitionGuardPolicy.RootKey("unrelated-root"),
                0, 0, 0));
            selected = ImportantMemorySelector.SelectV2(
                query, new List<MemoryRecallCandidateSnapshot> { extraState });
            AssertEqual("recallV2.guard.extra-state-no-overcommit",
                MemoryRecallRejectReasons.GuardBypass,
                selected.report[0].rejectReason);
        }

        private static void TestRecallV2FrozenRevalidationAndPairedPrivacy()
        {
            MemoryRecallQueryV2 initiatorQuery = RecallQuery(
                "Owner_A", MemoryRecallWritingFormats.Full);
            MemoryRecallCandidateSnapshot sharedA = RecallCandidate(
                "a-shared", "Owner_A", "source-shared", 100);
            MemoryRecallCandidateSnapshot ownA = RecallCandidate(
                "a-own", "Owner_A", "source-a", 90);
            MemoryRecallCandidateSnapshot missingPair = RecallCandidate(
                "a-missing-pair", "Owner_A", "source-missing-pair", 130);
            MemoryRecallQueryV2 recipientQuery = RecallQuery(
                "Owner_B", MemoryRecallWritingFormats.Full);
            MemoryRecallCandidateSnapshot sharedB = RecallCandidate(
                "b-shared", "Owner_B", "source-shared", 110);
            MemoryRecallCandidateSnapshot ownB = RecallCandidate(
                "b-own", "Owner_B", "source-b", 80);
            MemoryRecallCandidateSnapshot crossOwner = RecallCandidate(
                "cross-owner", "Owner_A", "source-cross", 120);
            crossOwner.historicalText = string.Empty;
            AddPairGuard(sharedA, "Owner_B");
            AddPairGuard(ownA, "Owner_B");
            AddPairGuard(sharedB, "Owner_A");
            AddPairGuard(ownB, "Owner_A");
            AddPairGuard(crossOwner, "Owner_B");
            MemoryRecallPairedResultV2 paired = ImportantMemorySelector.SelectPairedV2(
                initiatorQuery,
                new List<MemoryRecallCandidateSnapshot> { missingPair, sharedA, ownA },
                recipientQuery,
                new List<MemoryRecallCandidateSnapshot> { sharedB, crossOwner, ownB });
            AssertEqual("recallV2.pair.initiator.full-cap", 2, paired.initiator.selected.Count);
            AssertEqual("recallV2.pair.missing-guard-no-bypass",
                MemoryRecallRejectReasons.GuardBypass,
                paired.initiator.report.First(row => row.recordId == "a-missing-pair").rejectReason);
            AssertEqual("recallV2.pair.recipient.private-distinct", 1, paired.recipient.selected.Count);
            AssertEqual("recallV2.pair.recipient-own", "b-own",
                paired.recipient.selected[0].candidate.recordId);
            AssertEqual("recallV2.pair.shared-rejected", MemoryRecallRejectReasons.CurrentEvent,
                paired.recipient.report.First(row => row.recordId == "b-shared").rejectReason);
            AssertEqual("recallV2.pair.cross-owner-rejected", MemoryRecallRejectReasons.OwnerMismatch,
                paired.recipient.report.First(row => row.recordId == "cross-owner").rejectReason);

            MemoryRecallQueryV2 balanced = RecallQuery(
                "Owner_A", MemoryRecallWritingFormats.Balanced);
            MemoryRecallCandidateSnapshot first = RecallCandidate(
                "frozen-first", "Owner_A", "source-first", 200);
            MemoryRecallCandidateSnapshot lower = RecallCandidate(
                "not-frozen", "Owner_A", "source-lower", 100);
            MemoryRecallSelectionResultV2 frozen = ImportantMemorySelector.SelectV2(
                balanced, new List<MemoryRecallCandidateSnapshot> { lower, first });
            AssertEqual("recallV2.freeze.balanced-one", 1, frozen.selected.Count);
            MemoryRecallCandidateSnapshot currentFirst = RecallCandidate(
                "frozen-first", "Owner_A", "source-first", 200);
            currentFirst.suppressed = true;
            MemoryRecallSelectionResultV2 revalidated = ImportantMemorySelector.RevalidateFrozenV2(
                frozen,
                balanced,
                new List<MemoryRecallCandidateSnapshot> { currentFirst, lower });
            AssertEqual("recallV2.freeze.no-replacement", 0, revalidated.selected.Count);
            AssertEqual("recallV2.freeze.suppressed", MemoryRecallRejectReasons.Suppressed,
                revalidated.report[0].rejectReason);

            currentFirst.suppressed = false;
            currentFirst.historicalText = "current player wording";
            currentFirst.representedSourceOccurrenceIds.Add("later-thread-source");
            revalidated = ImportantMemorySelector.RevalidateFrozenV2(
                frozen, balanced, new List<MemoryRecallCandidateSnapshot> { currentFirst });
            AssertEqual("recallV2.freeze.current-wording", "current player wording",
                revalidated.selected[0].candidate.historicalText);
            AssertEqual("recallV2.freeze.no-later-source-addition", 0,
                revalidated.selected[0].candidate.representedSourceOccurrenceIds.Count);

            MemoryRecallCandidateSnapshot crossOwnerDuplicate = RecallCandidate(
                "frozen-first", "Owner_B", "source-first", 999);
            revalidated = ImportantMemorySelector.RevalidateFrozenV2(
                frozen,
                balanced,
                new List<MemoryRecallCandidateSnapshot>
                {
                    crossOwnerDuplicate,
                    currentFirst
                });
            AssertEqual("recallV2.freeze.cross-owner-duplicate-ignored", 1,
                revalidated.selected.Count);
            AssertEqual("recallV2.freeze.owner-current-selected", "current player wording",
                revalidated.selected[0].candidate.historicalText);

            currentFirst.sourceOccurrenceId = "source-replaced";
            revalidated = ImportantMemorySelector.RevalidateFrozenV2(
                frozen, balanced, new List<MemoryRecallCandidateSnapshot> { currentFirst });
            AssertEqual("recallV2.freeze.identity-change-no-replacement", 0,
                revalidated.selected.Count);
            AssertEqual("recallV2.freeze.identity-change-rejected",
                MemoryRecallRejectReasons.FrozenCandidateMissing,
                revalidated.report[0].rejectReason);

            MemoryRecallQueryV2 otherOwnerQuery = RecallQuery(
                "Owner_B", MemoryRecallWritingFormats.Balanced);
            MemoryRecallCandidateSnapshot otherOwnerCurrent = RecallCandidate(
                "frozen-first", "Owner_B", "source-first", 200);
            revalidated = ImportantMemorySelector.RevalidateFrozenV2(
                frozen,
                otherOwnerQuery,
                new List<MemoryRecallCandidateSnapshot> { otherOwnerCurrent });
            AssertEqual("recallV2.freeze.cross-owner-envelope-no-read", 0,
                revalidated.selected.Count);
            AssertEqual("recallV2.freeze.cross-owner-envelope-rejected",
                MemoryRecallRejectReasons.InvalidQuery,
                revalidated.report[0].rejectReason);
        }

        private static void TestRecallV2FrozenSelectionSaveCodec()
        {
            MemoryRecallQueryV2 query = RecallQuery(
                "Owner_A", MemoryRecallWritingFormats.Balanced);
            string epoch = M6Epoch("Owner_A");
            query.ownerEpochToken = epoch;
            MemoryRecallCandidateSnapshot candidate = RecallCandidate(
                "saved-frozen", "Owner_A", "source-saved", 321);
            candidate.ownerEpochToken = epoch;
            candidate.recordGuard.ownerEpochToken = epoch;
            foreach (MemoryRepetitionGuardState guard in candidate.structuralGuardStates)
                guard.ownerEpochToken = epoch;
            candidate.sourceEventId = "event-source";
            MakeThreadCandidate(candidate, "root-at-event-time", "chapter-at-event-time");
            candidate.narrativeFitScore = 17;
            candidate.categories.Add(MemoryContractTokens.CategoryRelationships);
            candidate.topicKeys.Add("topic.saved");
            candidate.representedSourceOccurrenceIds.Add("represented-source");
            MemoryRecallSelectionResultV2 frozen = ImportantMemorySelector.SelectV2(
                query, new List<MemoryRecallCandidateSnapshot> { candidate });
            AssertEqual("recallV2.save-codec.selected", 1, frozen.selected.Count);

            string encoded = MemoryFrozenRecallSelectionCodec.Encode(frozen);
            AssertTrue("recallV2.save-codec.encoded", !string.IsNullOrEmpty(encoded));
            MemoryRecallSelectionResultV2 loaded =
                MemoryFrozenRecallSelectionCodec.Decode(encoded);
            AssertTrue("recallV2.save-codec.decoded", loaded != null);
            AssertEqual("recallV2.save-codec.owner", "Owner_A", loaded.ownerPawnId);
            AssertEqual("recallV2.save-codec.record", "saved-frozen",
                loaded.selected[0].candidate.recordId);
            AssertEqual("recallV2.save-codec.represented", "represented-source",
                loaded.selected[0].candidate.representedSourceOccurrenceIds[0]);
            AssertEqual("recallV2.save-codec.route", "Pawn_B",
                loaded.selected[0].candidate.exactRoutes[0].subjectId);

            MemoryRecallCandidateSnapshot current = RecallCandidate(
                "saved-frozen", "Owner_A", "source-saved", 999);
            MakeThreadCandidate(current, "root-at-event-time", "chapter-at-event-time");
            current.ownerEpochToken = epoch;
            current.recordGuard.ownerEpochToken = epoch;
            foreach (MemoryRepetitionGuardState guard in current.structuralGuardStates)
                guard.ownerEpochToken = epoch;
            current.historicalText = "current wording after reload";
            MemoryRecallSelectionResultV2 revalidated =
                ImportantMemorySelector.RevalidateFrozenV2(
                    loaded,
                    query,
                    new List<MemoryRecallCandidateSnapshot> { current });
            AssertEqual("recallV2.save-codec.same-shortlist", 1,
                revalidated.selected.Count);
            AssertEqual("recallV2.save-codec.current-wording",
                "current wording after reload",
                revalidated.selected[0].candidate.historicalText);
            AssertEqual("recallV2.save-codec.frozen-tick", 321L,
                revalidated.selected[0].candidate.originalEventTick);
            AssertTrue("recallV2.save-codec.trailing-rejected",
                MemoryFrozenRecallSelectionCodec.Decode(encoded + "x") == null);
        }

        private static void TestRecallV2CurrentTruthAndCurrentReleaseComparison()
        {
            MemoryRecallQueryV2 query = RecallQuery("Owner_A", MemoryRecallWritingFormats.Full);
            MemoryRecallCandidateSnapshot stale = RecallCandidate(
                "stale", "Owner_A", "source-stale", 300);
            stale.currentStateApplicable = true;
            stale.currentStateContradictsHistorical = true;
            stale.currentStateCanRender = false;
            MemoryRecallSelectionResultV2 result = ImportantMemorySelector.SelectV2(
                query, new List<MemoryRecallCandidateSnapshot> { stale });
            AssertEqual("recallV2.current.reject-stale",
                MemoryRecallRejectReasons.CurrentTruthUnavailable,
                result.report[0].rejectReason);

            MemoryRecallCandidateSnapshot missingCurrent = RecallCandidate(
                "missing-current", "Owner_A", "source-missing-current", 300);
            missingCurrent.currentStateApplicable = true;
            missingCurrent.currentStateCanRender = true;
            missingCurrent.currentStateText = "Now: current truth without provenance.";
            result = ImportantMemorySelector.SelectV2(
                query, new List<MemoryRecallCandidateSnapshot> { missingCurrent });
            AssertEqual("recallV2.current.required-provenance-no-bypass",
                MemoryRecallRejectReasons.CurrentTruthUnavailable,
                result.report[0].rejectReason);

            MemoryRecallCandidateSnapshot truthful = RecallCandidate(
                "truthful", "Owner_A", "source-truth", 301);
            truthful.historicalText = "Then: they were allies.";
            truthful.currentStateApplicable = true;
            truthful.currentStateContradictsHistorical = true;
            truthful.currentStateCanRender = true;
            truthful.currentStateText = "Now: they are hostile.";
            truthful.currentStateSourceId = "snapshot-1";
            result = ImportantMemorySelector.SelectV2(
                query, new List<MemoryRecallCandidateSnapshot> { truthful });
            MemoryRecallPromptLine rendered = ImportantMemoryLineRenderer.RenderV2(
                result.selected[0], 200, 200);
            AssertEqual("recallV2.current.history-separate", "Then: they were allies.",
                rendered.historicalText);
            AssertEqual("recallV2.current.truth-separate", "Now: they are hostile.",
                rendered.currentStateText);

            MemoryRecallCandidateSnapshot summary = RecallCandidate(
                "summary-wording", "Owner_A", "source-summary-wording", 302);
            summary.kind = MemoryContractTokens.KindSummary;
            summary.historicalText = "Deterministic filtered summary.";
            summary.summaryWording = new MemoryRecallSummaryWordingSnapshot
            {
                currentProjectionFingerprint = "projection-current",
                currentFormatRevision = 7,
                currentCategoryMask = 1,
                optionalWording = "Natural optional summary.",
                optionalFingerprint = "projection-current",
                optionalFormatRevision = 7,
                optionalCategoryMask = 1,
                optionalSucceeded = true
            };
            MemoryRecallSelectedCandidate selectedSummary = ImportantMemorySelector.SelectV2(
                query, new List<MemoryRecallCandidateSnapshot> { summary }).selected[0];
            summary.summaryWording.optionalWording = "Mutated after selection.";
            rendered = ImportantMemoryLineRenderer.RenderV2(selectedSummary, 200, 200);
            AssertEqual("recallV2.summary.exact-cache-reaches-natural-writing",
                "Natural optional summary.", rendered.historicalText);

            MemoryRecallCandidateSnapshot staleSummary = RecallCandidate(
                "summary-stale", "Owner_A", "source-summary-stale", 303);
            staleSummary.kind = MemoryContractTokens.KindSummary;
            staleSummary.historicalText = "Deterministic stale fallback.";
            staleSummary.summaryWording = new MemoryRecallSummaryWordingSnapshot
            {
                currentProjectionFingerprint = "projection-current",
                currentFormatRevision = 7,
                currentCategoryMask = 1,
                optionalWording = "Stale optional summary.",
                optionalFingerprint = "projection-old",
                optionalFormatRevision = 7,
                optionalCategoryMask = 1,
                optionalSucceeded = true
            };
            selectedSummary = ImportantMemorySelector.SelectV2(
                query, new List<MemoryRecallCandidateSnapshot> { staleSummary }).selected[0];
            rendered = ImportantMemoryLineRenderer.RenderV2(selectedSummary, 200, 200);
            AssertEqual("recallV2.summary.stale-cache-keeps-deterministic",
                "Deterministic stale fallback.", rendered.historicalText);

            KnowledgeSelectionResult legacy = new KnowledgeSelectionResult();
            legacy.selected.Add(Record("legacy-record", 1, subjectKey: "part:Leg"));
            MemoryRecallShadowComparison comparison = ImportantMemorySelector.CompareLegacyAndV2(
                legacy, result);
            AssertEqual("recallV2.release.build-state", MemorySystemActivationGate.CurrentRelease,
                comparison.buildState);
            AssertTrue("recallV2.release.differs", comparison.differs);
            AssertTrue("recallV2.release.publishes-current", !comparison.publishesLegacy);
            AssertEqual("recallV2.release.current-id", "truthful",
                comparison.publishedRecordIds[0]);
        }

        /// <summary>
        /// Pure selection accepts only detached adapter snapshots, so every malformed, absent,
        /// duplicate, over-ceiling, or unknown-future field must reject one row instead of throwing
        /// or letting a neighbouring row inherit the damage. Also pins the four-format selection cap
        /// and the independence of the §10.3 pawn-background column.
        /// </summary>
        private static void TestRecallV2AdversarialIdentityAndCapMatrix()
        {
            // A represented-source list drives a hard exclusion. An absent list means "unknown", so
            // it must fail closed rather than crash pure selection or read as "represents nothing".
            MemoryRecallCandidateSnapshot absentSources = RecallCandidate(
                "absent-sources", "Owner_A", "source-absent", 500);
            absentSources.representedSourceOccurrenceIds = null;
            MemoryRecallCandidateSnapshot healthyNeighbour = RecallCandidate(
                "healthy-neighbour", "Owner_A", "source-healthy", 499);
            MemoryRecallSelectionResultV2 result = ImportantMemorySelector.SelectV2(
                RecallQuery("Owner_A", MemoryRecallWritingFormats.Full),
                new List<MemoryRecallCandidateSnapshot> { absentSources, healthyNeighbour });
            AssertEqual("recallV2.sources.absent-list-fails-closed",
                MemoryRecallRejectReasons.MissingOrCorrupt,
                result.report.First(row => row.recordId == "absent-sources").rejectReason);
            AssertEqual("recallV2.sources.absent-list-does-not-abort-selection", 1,
                result.selected.Count);
            AssertEqual("recallV2.sources.neighbour-survives", "healthy-neighbour",
                result.selected[0].candidate.recordId);

            MemoryRecallCandidateSnapshot duplicateSource = RecallCandidate(
                "duplicate-source", "Owner_A", "source-duplicate", 501);
            duplicateSource.representedSourceOccurrenceIds.Add("represented-1");
            duplicateSource.representedSourceOccurrenceIds.Add("represented-1");
            MemoryRecallCandidateSnapshot selfRepresenting = RecallCandidate(
                "self-representing", "Owner_A", "source-self-rep", 502);
            selfRepresenting.representedSourceOccurrenceIds.Add("source-self-rep");
            MemoryRecallCandidateSnapshot overCeilingRecord = RecallCandidate(
                new string('r', MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters + 1),
                "Owner_A",
                "source-over-ceiling",
                503);
            MemoryRecallCandidateSnapshot malformedSource = RecallCandidate(
                "malformed-source", "Owner_A", "source-\uD800-lone", 504);
            result = ImportantMemorySelector.SelectV2(
                RecallQuery("Owner_A", MemoryRecallWritingFormats.Full),
                new List<MemoryRecallCandidateSnapshot>
                {
                    duplicateSource, selfRepresenting, overCeilingRecord, malformedSource
                });
            AssertEqual("recallV2.identity.adversarial-selects-none", 0, result.selected.Count);
            AssertTrue("recallV2.identity.adversarial-all-corrupt",
                result.report.All(row =>
                    row.rejectReason == MemoryRecallRejectReasons.MissingOrCorrupt));

            // Record/source/root identities are already-encoded or opaque composite values, not
            // raw pawn IDs. A valid value just above the raw ceiling must remain recallable while
            // the embedded-composite ceiling above stays absolute.
            string compositeRecord = new string(
                'c',
                MemoryIdentityCodec.MaximumRawIdentityCharacters + 1);
            string compositeSource = new string(
                's',
                MemoryIdentityCodec.MaximumRawIdentityCharacters + 1);
            string compositeRoot = new string(
                't',
                MemoryIdentityCodec.MaximumRawIdentityCharacters + 1);
            string compositeNovelty = new string(
                'n',
                MemoryIdentityCodec.MaximumRawIdentityCharacters + 1);
            MemoryRecallCandidateSnapshot compositeCandidate = RecallCandidate(
                compositeRecord, "Owner_A", compositeSource, 504);
            MakeThreadCandidate(compositeCandidate, compositeRoot, compositeNovelty);
            result = ImportantMemorySelector.SelectV2(
                RecallQuery("Owner_A", MemoryRecallWritingFormats.Full),
                new List<MemoryRecallCandidateSnapshot>
                {
                    compositeCandidate
                });
            AssertEqual("recallV2.identity.composite-above-raw-accepted", 1,
                result.selected.Count);

            MemoryRecallQueryV2 missingExclusionList = RecallQuery(
                "Owner_A", MemoryRecallWritingFormats.Full);
            missingExclusionList.excludedSourceOccurrenceIds = null;
            AssertEqual("recallV2.exclusion.null-list-fails-closed",
                MemoryRecallRejectReasons.InvalidQuery,
                ImportantMemorySelector.SelectV2(
                    missingExclusionList,
                    new List<MemoryRecallCandidateSnapshot>
                    {
                        RecallCandidate("null-exclusion", "Owner_A", "source-null", 504)
                    }).report[0].rejectReason);

            // A different owner may legitimately hold the same record ID. That lookalike must be
            // rejected on ownership alone and must never mark the owner's own row ambiguous.
            MemoryRecallCandidateSnapshot ownRow = RecallCandidate(
                "shared-record-id", "Owner_A", "source-own", 505);
            MemoryRecallCandidateSnapshot lookalike = RecallCandidate(
                "shared-record-id", "Owner_B", "source-lookalike", 506);
            result = ImportantMemorySelector.SelectV2(
                RecallQuery("Owner_A", MemoryRecallWritingFormats.Full),
                new List<MemoryRecallCandidateSnapshot> { lookalike, ownRow });
            AssertEqual("recallV2.identity.cross-owner-lookalike-not-ambiguous", 1,
                result.selected.Count);
            AssertEqual("recallV2.identity.cross-owner-lookalike-rejected",
                MemoryRecallRejectReasons.OwnerMismatch,
                result.report.First(row => row.rejectReason.Length > 0).rejectReason);

            // §10.3 selection column: Full/Balanced/Compact/Off are exactly 2/1/0/0.
            string[] formats =
            {
                MemoryRecallWritingFormats.Full,
                MemoryRecallWritingFormats.Balanced,
                MemoryRecallWritingFormats.Compact,
                MemoryRecallWritingFormats.Off
            };
            int[] expectedLines = { 2, 1, 0, 0 };
            for (int index = 0; index < formats.Length; index++)
            {
                MemoryRecallSelectionResultV2 formatted = ImportantMemorySelector.SelectV2(
                    RecallQuery("Owner_A", formats[index]),
                    new List<MemoryRecallCandidateSnapshot>
                    {
                        RecallCandidate("cap-a", "Owner_A", "source-cap-a", 600),
                        RecallCandidate("cap-b", "Owner_A", "source-cap-b", 599),
                        RecallCandidate("cap-c", "Owner_A", "source-cap-c", 598)
                    });
                AssertEqual("recallV2.select.cap." + formats[index],
                    expectedLines[index], formatted.selected.Count);
                AssertEqual("recallV2.select.cap-never-overflows." + formats[index],
                    expectedLines[index], formatted.lineCap);
                if (expectedLines[index] == 0)
                {
                    AssertTrue("recallV2.select.memory-free-format." + formats[index],
                        formatted.report.All(row =>
                            row.rejectReason == MemoryRecallRejectReasons.FormatDisabled));
                }
            }

            // `Use memories in writing` off silences episodic recall while the separate background
            // switch keeps its own Full/Balanced eligibility.
            MemoryRecallQueryV2 episodicOff = RecallQuery("Owner_A", MemoryRecallWritingFormats.Full);
            episodicOff.useMemoriesInWriting = false;
            result = ImportantMemorySelector.SelectV2(
                episodicOff,
                new List<MemoryRecallCandidateSnapshot>
                {
                    RecallCandidate("episodic-off", "Owner_A", "source-episodic-off", 601)
                });
            AssertEqual("recallV2.select.episodic-off-none", 0, result.selected.Count);
            AssertEqual("recallV2.select.episodic-off-reason",
                MemoryRecallRejectReasons.FormatDisabled, result.report[0].rejectReason);
            AssertTrue("recallV2.background.survives-episodic-off",
                MemoryContextPrompt.AllowsPawnBackground(MemoryRecallWritingFormats.Full, true)
                && MemoryContextPrompt.AllowsPawnBackground(
                    MemoryRecallWritingFormats.Balanced, true));

            MemoryRecallQueryV2 unknownConsumer = RecallQuery(
                "Owner_A", MemoryRecallWritingFormats.Full);
            unknownConsumer.consumerId = "future_consumer";
            AssertEqual("recallV2.consumer.unknown-fails-closed",
                MemoryRecallRejectReasons.UnknownConsumer,
                ImportantMemorySelector.SelectV2(
                    unknownConsumer,
                    new List<MemoryRecallCandidateSnapshot>
                    {
                        RecallCandidate("unknown-consumer", "Owner_A", "source-unknown-c", 602)
                    }).report[0].rejectReason);
            MemoryRecallQueryV2 unknownFormat = RecallQuery("Owner_A", "Verbose");
            AssertEqual("recallV2.format.unknown-fails-closed",
                MemoryRecallRejectReasons.InvalidQuery,
                ImportantMemorySelector.SelectV2(
                    unknownFormat,
                    new List<MemoryRecallCandidateSnapshot>
                    {
                        RecallCandidate("unknown-format", "Owner_A", "source-unknown-f", 603)
                    }).report[0].rejectReason);

            // A pawn is never its own paired counterpart, so neither POV may read the other list.
            MemoryRecallPairedResultV2 samePawn = ImportantMemorySelector.SelectPairedV2(
                RecallQuery("Owner_A", MemoryRecallWritingFormats.Full),
                new List<MemoryRecallCandidateSnapshot>
                {
                    RecallCandidate("same-owner-a", "Owner_A", "source-same-a", 604)
                },
                RecallQuery("Owner_A", MemoryRecallWritingFormats.Full),
                new List<MemoryRecallCandidateSnapshot>
                {
                    RecallCandidate("same-owner-b", "Owner_A", "source-same-b", 605)
                });
            AssertEqual("recallV2.pair.same-owner-initiator-none", 0,
                samePawn.initiator.selected.Count);
            AssertEqual("recallV2.pair.same-owner-recipient-none", 0,
                samePawn.recipient.selected.Count);
            AssertTrue("recallV2.pair.same-owner-invalid",
                samePawn.initiator.report[0].rejectReason == MemoryRecallRejectReasons.InvalidQuery
                && samePawn.recipient.report[0].rejectReason
                    == MemoryRecallRejectReasons.InvalidQuery);

            MemoryRecallCandidateSnapshot duplicateRequirement = RecallCandidate(
                "duplicate-requirement", "Owner_A", "source-duplicate-req", 606);
            duplicateRequirement.requiredStructuralGuards.Add(new MemoryGuardIdentity
            {
                guardKind = MemoryRepetitionGuardKinds.Subject,
                guardKey = MemoryRepetitionGuardPolicy.SubjectKey(
                    MemoryContractTokens.SubjectPawn, "Pawn_B")
            });
            MemoryRecallCandidateSnapshot duplicateState = RecallCandidate(
                "duplicate-state", "Owner_A", "source-duplicate-state", 607);
            duplicateState.structuralGuardStates.Add(GuardState(
                "epoch-1",
                MemoryRepetitionGuardKinds.Subject,
                MemoryRepetitionGuardPolicy.SubjectKey(MemoryContractTokens.SubjectPawn, "Pawn_B"),
                0, 0, 0));
            MemoryRecallCandidateSnapshot recordKindState = RecallCandidate(
                "record-kind-state", "Owner_A", "source-record-kind", 608);
            recordKindState.structuralGuardStates.Add(GuardState(
                "epoch-1",
                MemoryRepetitionGuardKinds.Record,
                MemoryRepetitionGuardPolicy.RecordKey("record-kind-state"),
                0, 0, 0));
            result = ImportantMemorySelector.SelectV2(
                RecallQuery("Owner_A", MemoryRecallWritingFormats.Full),
                new List<MemoryRecallCandidateSnapshot>
                {
                    duplicateRequirement, duplicateState, recordKindState
                });
            AssertEqual("recallV2.guard.duplicate-and-extra-kinds-select-none", 0,
                result.selected.Count);
            AssertTrue("recallV2.guard.duplicate-and-extra-kinds-no-bypass",
                result.report.All(row =>
                    row.rejectReason == MemoryRecallRejectReasons.GuardBypass));

            // Provenance and text describe each other in both directions. A current-state row that
            // cannot fit its own bounded budget drops with its diagnostic, and the still-truthful
            // historical sentence survives instead of losing the whole line at projection time.
            MemoryRecallCandidateSnapshot boundedTruth = RecallCandidate(
                "bounded-truth", "Owner_A", "source-bounded-truth", 609);
            boundedTruth.historicalText = "Then: they shared a meal.";
            boundedTruth.currentStateApplicable = true;
            boundedTruth.currentStateCanRender = true;
            boundedTruth.currentStateText = "Now: they still share meals.";
            boundedTruth.currentStateSourceId = "snapshot-bounded";
            MemoryRecallSelectedCandidate boundedSelected = ImportantMemorySelector.SelectV2(
                RecallQuery("Owner_A", MemoryRecallWritingFormats.Full),
                new List<MemoryRecallCandidateSnapshot> { boundedTruth }).selected[0];
            AssertEqual("recallV2.current.plan-has-both-rows", 2, boundedSelected.diagnostics.Count);
            MemoryRecallPromptLine droppedCurrent = ImportantMemoryLineRenderer.RenderV2(
                boundedSelected, 200, 0);
            AssertEqual("recallV2.current.dropped-text", string.Empty,
                droppedCurrent.currentStateText);
            AssertEqual("recallV2.current.dropped-provenance-too", 1,
                droppedCurrent.diagnostics.Count);
            AssertEqual("recallV2.current.history-still-rendered", "Then: they shared a meal.",
                droppedCurrent.historicalText);
            AssertEqual("recallV2.current.history-survives-projection", 1,
                MemoryContextPrompt.ProjectV2(
                    MemoryRecallWritingFormats.Full,
                    string.Empty,
                    "current-state instruction",
                    new List<MemoryRecallPromptLine> { droppedCurrent },
                    1000, 2, 8, 16).lines.Count);

            // An unrenderable current state is never planned as provenance in the first place.
            MemoryRecallCandidateSnapshot unrenderable = RecallCandidate(
                "unrenderable-current", "Owner_A", "source-unrenderable", 610);
            unrenderable.currentStateApplicable = true;
            unrenderable.currentStateCanRender = false;
            unrenderable.currentStateText = "Now: unrenderable.";
            unrenderable.currentStateSourceId = "snapshot-unrenderable";
            AssertEqual("recallV2.current.unrenderable-rejected-before-ranking",
                MemoryRecallRejectReasons.CurrentTruthUnavailable,
                ImportantMemorySelector.SelectV2(
                    RecallQuery("Owner_A", MemoryRecallWritingFormats.Full),
                    new List<MemoryRecallCandidateSnapshot> { unrenderable }).report[0].rejectReason);
        }

        private static MemoryRecallQueryV2 RecallQuery(string ownerPawnId, string writingFormat)
        {
            MemoryRecallQueryV2 query = new MemoryRecallQueryV2
            {
                ownerPawnId = ownerPawnId,
                ownerEpochToken = "epoch-1",
                consumerId = MemoryRecallConsumerRegistry.OrdinaryDiary,
                writingFormat = writingFormat,
                currentEventId = "event-current",
                currentSourceOccurrenceId = "source-current",
                repetitionPolicy = RecallRepetitionPolicy()
            };
            query.enabledCategories.Add(MemoryContractTokens.CategoryPersonal);
            query.exactRoutes.Add(RecallRoute("Pawn_B"));
            return query;
        }

        private static MemoryRepetitionPolicySnapshot RecallRepetitionPolicy()
        {
            return new MemoryRepetitionPolicySnapshot
            {
                currentTick = 600000,
                completedDiaryEntryOrdinal = 10,
                ticksPerDay = 60000,
                memoryReuseDays = 5,
                memoryRevisitEntryCount = 3,
                recordMinimumReuseDays = 1,
                recordMinimumEntryDistance = 1,
                rootMinimumEntryDistance = 1,
                subjectMinimumEntryDistance = 1,
                pairMinimumEntryDistance = 1,
                noveltyMinimumEntryDistance = 1
            };
        }

        private static MemoryRecallCandidateSnapshot RecallCandidate(
            string recordId,
            string ownerPawnId,
            string sourceOccurrenceId,
            long tick)
        {
            MemoryRecallCandidateSnapshot candidate = new MemoryRecallCandidateSnapshot
            {
                ownerPawnId = ownerPawnId,
                ownerEpochToken = "epoch-1",
                recordId = recordId,
                sourceOccurrenceId = sourceOccurrenceId,
                kind = MemoryContractTokens.KindEvent,
                importance = MemoryContractTokens.ImportanceRegular,
                originalEventTick = tick,
                historicalText = "historical " + recordId,
                recordGuard = GuardState(
                    "epoch-1",
                    MemoryRepetitionGuardKinds.Record,
                    MemoryRepetitionGuardPolicy.RecordKey(recordId),
                    0, 0, 0)
            };
            candidate.categories.Add(MemoryContractTokens.CategoryPersonal);
            candidate.exactRoutes.Add(RecallRoute("Pawn_B"));
            AddSubjectGuard(candidate, "Pawn_B");
            return candidate;
        }

        private static MemoryRecallRouteIdentity RecallRoute(string pawnId)
        {
            return new MemoryRecallRouteIdentity
            {
                routeKind = MemoryRecallRouteKinds.Participant,
                subjectKind = MemoryContractTokens.SubjectPawn,
                subjectId = pawnId,
                routeKey = "route-" + pawnId
            };
        }

        private static void AddSubjectGuard(
            MemoryRecallCandidateSnapshot candidate,
            string pawnId)
        {
            string key = MemoryRepetitionGuardPolicy.SubjectKey(
                MemoryContractTokens.SubjectPawn,
                pawnId);
            candidate.requiredStructuralGuards.Add(new MemoryGuardIdentity
            {
                guardKind = MemoryRepetitionGuardKinds.Subject,
                guardKey = key
            });
            candidate.structuralGuardStates.Add(GuardState(
                candidate.ownerEpochToken,
                MemoryRepetitionGuardKinds.Subject,
                key,
                0, 0, 0));
        }

        private static void AddPairGuard(
            MemoryRecallCandidateSnapshot candidate,
            string counterpartPawnId)
        {
            string key = MemoryRepetitionGuardPolicy.PairKey(
                candidate.ownerPawnId,
                counterpartPawnId);
            candidate.requiredStructuralGuards.Add(new MemoryGuardIdentity
            {
                guardKind = MemoryRepetitionGuardKinds.Pair,
                guardKey = key
            });
            candidate.structuralGuardStates.Add(GuardState(
                candidate.ownerEpochToken,
                MemoryRepetitionGuardKinds.Pair,
                key,
                0, 0, 0));
        }

        private static void MakeThreadCandidate(
            MemoryRecallCandidateSnapshot candidate,
            string rootId,
            string noveltyId)
        {
            candidate.isThreadMember = true;
            candidate.rootId = rootId;
            candidate.chapterOrNoveltyId = noveltyId;
            AddStructuralGuard(candidate, MemoryRepetitionGuardKinds.Root,
                MemoryRepetitionGuardPolicy.RootKey(rootId));
            AddStructuralGuard(candidate, MemoryRepetitionGuardKinds.Novelty,
                MemoryRepetitionGuardPolicy.NoveltyKey(rootId, noveltyId));
        }

        private static void AddStructuralGuard(
            MemoryRecallCandidateSnapshot candidate,
            string kind,
            string key)
        {
            candidate.requiredStructuralGuards.Add(new MemoryGuardIdentity
            {
                guardKind = kind,
                guardKey = key
            });
            candidate.structuralGuardStates.Add(GuardState(
                candidate.ownerEpochToken, kind, key, 0, 0, 0));
        }

        private static MemoryRepetitionGuardState GuardState(
            string epoch,
            string kind,
            string key,
            long lastTick,
            long lastEntry,
            long count)
        {
            return new MemoryRepetitionGuardState
            {
                ownerEpochToken = epoch,
                guardKind = kind,
                guardKey = key,
                lastAutomaticIncludedTick = lastTick,
                lastAutomaticIncludedEntryOrdinal = lastEntry,
                automaticInclusionCount = count
            };
        }

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

            List<XElement> topicDefs = topicsDoc.Root
                .Elements("PawnDiary.DiaryCultureTopicDef").ToList();
            List<string> topicKeys = topicDefs
                .Select(def => (string)def.Element("topicKey")).ToList();
            AssertEqual("cultureXml.topicCount", 14, topicKeys.Count);
            AssertEqual("cultureXml.topicUnique", 14,
                new HashSet<string>(topicKeys, StringComparer.OrdinalIgnoreCase).Count);

            string ruTopicPath = Path.Combine(root, "Languages", "Russian (Русский)",
                "DefInjected", "PawnDiary.DiaryCultureTopicDef", "DiaryCultureTopicDefs.xml");
            XDocument ruTopics = XDocument.Load(ruTopicPath);
            foreach (XElement topicDef in topicDefs)
            {
                string defName = (string)topicDef.Element("defName");
                List<string> terms = ListItems(topicDef, "triggerTextTerms").ToList();
                AssertTrue("cultureXml." + defName + ".textTerms", terms.Count > 0);
                for (int i = 0; i < terms.Count; i++)
                {
                    AssertTrue("cultureXml." + defName + ".textTerm." + i,
                        CultureTextTermMatcher.IsValidPattern(terms[i]));
                    string tag = defName + ".triggerTextTerms." + i;
                    XElement translated = ruTopics.Root.Element(tag);
                    AssertTrue("cultureXml.ru." + tag,
                        translated != null
                        && CultureTextTermMatcher.IsValidPattern(translated.Value));
                }
            }

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
                AssertEqual("cultureXml." + culture + ".clauseCount", 14, clauses.Count);
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
                for (int i = 0; i < 14; i++)
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

        private static int TextOccurrences(string source, string value)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value)) return 0;
            int count = 0;
            int offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }
            return count;
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

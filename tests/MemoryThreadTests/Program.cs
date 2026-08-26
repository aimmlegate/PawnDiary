// Standalone, no-RimWorld checks for the unified memory system's canonical identity vocabulary and
// length-prefixed key grammar. Explicit byte-shape assertions make identity changes review-visible.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using PawnDiary;

namespace MemoryThreadTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            try
            {
                TestSegmentCodec();
                TestMalformedSegments();
                TestSocialReflectionCompatibility();
                TestStableTokens();
                TestRootIdentity();
                TestRecordAndChapterIdentity();
                TestFactionAndSummaryIdentity();
                TestSourceOccurrenceFallback();
                TestEpochAllocationIdentity();
                TestSyntheticAndRepairIdentity();
                TestRequestIdentity();
                TestMemoryDispatchPolicy();
                TestOptionalMemoryAiPolicy();
                TestAcceptedPromptRetention();
                TestExactRoutePolicy();
                TestFactGrammar();
                TestSettingsAndCapacityContracts();
                TestShippedXmlContractsAndReachability();
                TestM0CatalogShape();
                TestSavedScalarSchemaRegistry();
                TestOwnerEnvelopeSchemaPolicy();
                TestSummaryFingerprint();
                TestIdentityCarrierRegistry();
                TestLogicalPayloadSizer();
                TestActivePayloadBudget();
                TestLegacyMigrationDryRun();
                TestImportedBudget();
                assertions += MemoryM4Fixtures.Run();
                assertions += MemoryM5Fixtures.Run();
                assertions += MemoryM8Fixtures.Run();
                assertions += MemoryM9Fixtures.Run();

                Console.WriteLine("MemoryThreadTests passed " + assertions + " assertions.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        private static void TestSegmentCodec()
        {
            string[] values =
            {
                string.Empty,
                "plain",
                "2::|[]{}",
                "Жизнь café 😀",
                new string('x', MemoryIdentityCodec.MaximumRawIdentityCharacters),
                "9223372036854775807"
            };
            foreach (string expected in values)
            {
                string encoded = OrdinalSegmentCodec.Segment(expected);
                int offset = 0;
                string actual;
                AssertTrue("segment.read." + expected.Length,
                    OrdinalSegmentCodec.TryReadCanonicalSegment(
                        encoded,
                        ref offset,
                        expected.Length,
                        expected.Length == 0,
                        out actual));
                AssertEqual("segment.value." + expected.Length, expected, actual);
                AssertEqual("segment.consumed." + expected.Length, encoded.Length, offset);
            }

            AssertEqual("segment.golden.empty", "0:", OrdinalSegmentCodec.Segment(string.Empty));
            AssertEqual("segment.golden.delimiters", "8:2::|[]{}",
                OrdinalSegmentCodec.Segment("2::|[]{}"));
        }

        private static void TestMalformedSegments()
        {
            string[] malformed =
            {
                string.Empty,
                ":x",
                "-1:x",
                "+1:x",
                "01:x",
                "1",
                "2:x",
                "2147483648:x",
                "1:\uD800",
                "1:\uDC00"
            };
            foreach (string value in malformed)
            {
                int offset = 0;
                string ignored;
                AssertTrue("segment.reject." + Escape(value),
                    !OrdinalSegmentCodec.TryReadCanonicalSegment(
                        value,
                        ref offset,
                        MemoryIdentityCodec.MaximumRawIdentityCharacters,
                        true,
                        out ignored));
            }

            int emptyOffset = 0;
            string empty;
            AssertTrue("segment.empty.disallowed",
                !OrdinalSegmentCodec.TryReadCanonicalSegment(
                    "0:", ref emptyOffset, 0, false, out empty));

            int overOffset = 0;
            string over;
            AssertTrue("segment.over-ceiling",
                !OrdinalSegmentCodec.TryReadCanonicalSegment(
                    "2:xx", ref overOffset, 1, true, out over));
        }

        private static void TestSocialReflectionCompatibility()
        {
            AssertEqual(
                "social.source.byte-golden",
                "7:Entry:15:123454:Chat6:Pawn_A6:Pawn_B",
                SocialReflectionPolicy.SourceKey("Entry:1", 12345, "Chat", "Pawn_A", "Pawn_B"));
            AssertEqual(
                "social.pair.byte-golden",
                "6:Pawn_A6:Pawn_B",
                SocialReflectionPolicy.PairKey("Pawn_B", "Pawn_A"));
            AssertTrue("social.pair.contains.first",
                SocialReflectionPolicy.PairKeyContainsPawn("6:Pawn_A6:Pawn_B", "Pawn_A"));
            AssertTrue("social.pair.contains.second",
                SocialReflectionPolicy.PairKeyContainsPawn("6:Pawn_A6:Pawn_B", "Pawn_B"));
            // The extracted compatibility reader intentionally preserves the shipped parser's
            // permissive leading-zero behavior for legacy pair keys.
            AssertTrue("social.pair.legacy-leading-zero",
                SocialReflectionPolicy.PairKeyContainsPawn("06:Pawn_A6:Pawn_B", "Pawn_A"));
        }

        private static void TestStableTokens()
        {
            AssertTrue("tokens.kind.event", MemoryContractTokens.IsKnownKind("event"));
            AssertTrue("tokens.kind.case", !MemoryContractTokens.IsKnownKind("Event"));
            AssertTrue("tokens.importance.high", MemoryContractTokens.IsKnownImportance("high"));
            AssertTrue("tokens.importance.future", !MemoryContractTokens.IsKnownImportance("critical"));
            AssertTrue("tokens.category.family", MemoryContractTokens.IsKnownCategory("family"));
            AssertTrue("tokens.category.unknown", !MemoryContractTokens.IsKnownCategory("social"));
            AssertTrue("tokens.root.pawn", MemoryContractTokens.IsKnownRootSubjectKind("pawn"));
            AssertTrue("tokens.root.family-not-kind",
                !MemoryContractTokens.IsKnownRootSubjectKind("family"));
            AssertEqual("tokens.stream.count", 10, MemoryContractTokens.StreamSubjectTokens().Count);
            AssertTrue("tokens.stream.body",
                MemoryContractTokens.IsKnownStreamSubjectToken("body_history"));
            AssertTrue("tokens.stream.unknown",
                !MemoryContractTokens.IsKnownStreamSubjectToken("body-history"));
            AssertTrue("tokens.summary.rolling",
                MemoryContractTokens.IsKnownSummaryRole("rolling"));
            AssertTrue("tokens.summary.future",
                !MemoryContractTokens.IsKnownSummaryRole("future"));

            AssertEqual("request.states", "staged/activated/invocation_committed/settlement_pending",
                string.Join("/", MemoryRequestStateMachineContracts.States()));
            AssertEqual("request.attempt-states",
                "prepared/invocation_committed/receipt_applied/terminal_pending",
                string.Join("/", MemoryRequestStateMachineContracts.AttemptStates()));
            AssertTrue("request.forward.stage-activate",
                MemoryRequestStateMachineContracts.CanTransition("staged", "activated"));
            AssertTrue("request.forward.invocation-settlement",
                MemoryRequestStateMachineContracts.CanTransition(
                    "invocation_committed", "settlement_pending"));
            AssertTrue("request.backward.reject",
                !MemoryRequestStateMachineContracts.CanTransition("settlement_pending", "staged"));
            AssertTrue("request.skip-send.reject",
                !MemoryRequestStateMachineContracts.CanTransition("staged", "invocation_committed"));
        }

        private static void TestRootIdentity()
        {
            MemoryRootIdentity root = Root("Pawn_A", Epoch(1), "pawn", "Pawn_B");
            string rootId;
            AssertTrue("root.create", MemoryIdentityCodec.TryCreateRootId(root, out rootId));
            AssertEqual(
                "root.golden",
                "14:memory-root-v16:Pawn_A21:15:memory-epoch-v11:14:pawn6:Pawn_B",
                rootId);

            MemoryRootIdentity parsed;
            AssertTrue("root.parse", MemoryIdentityCodec.TryParseRootId(rootId, out parsed));
            AssertEqual("root.parse.owner", root.ownerPawnId, parsed.ownerPawnId);
            AssertEqual("root.parse.epoch", root.ownerEpochToken, parsed.ownerEpochToken);
            AssertEqual("root.parse.kind", root.primarySubjectKind, parsed.primarySubjectKind);
            AssertEqual("root.parse.subject", root.primarySubjectId, parsed.primarySubjectId);

            string caseVariant;
            AssertTrue("root.case.create",
                MemoryIdentityCodec.TryCreateRootId(
                    Root("Pawn_A", Epoch(1), "pawn", "pawn_b"), out caseVariant));
            AssertTrue("root.case.distinct", !string.Equals(rootId, caseVariant, StringComparison.Ordinal));

            string changedOwner;
            string changedEpoch;
            string changedKind;
            string changedSubject;
            AssertTrue("root.owner.distinguishes",
                MemoryIdentityCodec.TryCreateRootId(
                    Root("Pawn_C", Epoch(1), "pawn", "Pawn_B"), out changedOwner)
                && changedOwner != rootId);
            AssertTrue("root.epoch.distinguishes",
                MemoryIdentityCodec.TryCreateRootId(
                    Root("Pawn_A", Epoch(2), "pawn", "Pawn_B"), out changedEpoch)
                && changedEpoch != rootId);
            AssertTrue("root.kind.distinguishes",
                MemoryIdentityCodec.TryCreateRootId(
                    Root("Pawn_A", Epoch(1), "stream", "body_history"), out changedKind)
                && changedKind != rootId);
            AssertTrue("root.subject.distinguishes",
                MemoryIdentityCodec.TryCreateRootId(
                    Root("Pawn_A", Epoch(1), "pawn", "Pawn_C"), out changedSubject)
                && changedSubject != rootId);

            AssertRejectsRoot("root.self", Root("Pawn_A", Epoch(1), "pawn", "Pawn_A"));
            AssertRejectsRoot("root.blank", Root("Pawn_A", Epoch(1), "pawn", " "));
            AssertRejectsRoot("root.unknownKind", Root("Pawn_A", Epoch(1), "family", "Pawn_B"));
            AssertRejectsRoot("root.unpaired", Root("Pawn_A", Epoch(1), "pawn", "\uD800"));
            AssertRejectsRoot("root.noncanonical-epoch", Root("Pawn_A", "epoch:1", "pawn", "Pawn_B"));
            AssertRejectsRoot("root.raw-cap+1",
                Root(new string('a', MemoryIdentityCodec.MaximumRawIdentityCharacters + 1),
                    Epoch(1), "pawn", "Pawn_B"));
            AssertRejectsRoot("root.composite-cap+1",
                Root("Pawn_A", Epoch(1), "pawn",
                    new string('s', MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters + 1)));
            AssertRejectsRoot("root.stream-not-allowlisted",
                Root("Pawn_A", Epoch(1), "stream", "invented_stream"));
            AssertRejectsRoot("root.faction-not-canonical",
                Root("Pawn_A", Epoch(1), "faction", "Faction_17"));

            AssertTrue("root.trailing.reject",
                !MemoryIdentityCodec.TryParseRootId(rootId + "1:x", out parsed));
            AssertTrue("root.wrong-domain.reject",
                !MemoryIdentityCodec.TryParseRootId(
                    rootId.Replace("memory-root-v1", "memory-roof-v1"), out parsed));
            AssertTrue("root.noncanonical-length.reject",
                !MemoryIdentityCodec.TryParseRootId("014:memory-root-v1" + rootId.Substring(17), out parsed));
        }

        private static void TestRecordAndChapterIdentity()
        {
            MemoryRecordIdentity record = new MemoryRecordIdentity
            {
                ownerPawnId = "Pawn_A",
                ownerEpochToken = Epoch(1),
                sourceOccurrenceId = "event:42",
                captureRuleId = "rule.birth",
                factDiscriminator = "child"
            };
            string recordId;
            AssertTrue("record.create", MemoryIdentityCodec.TryCreateRecordId(record, out recordId));
            AssertEqual(
                "record.golden",
                "16:memory-record-v16:Pawn_A21:15:memory-epoch-v11:18:event:4210:rule.birth5:child",
                recordId);

            MemoryRecordIdentity parsed;
            AssertTrue("record.parse", MemoryIdentityCodec.TryParseRecordId(recordId, out parsed));
            AssertEqual("record.parse.source", record.sourceOccurrenceId, parsed.sourceOccurrenceId);
            AssertEqual("record.parse.rule", record.captureRuleId, parsed.captureRuleId);
            AssertEqual("record.parse.fact", record.factDiscriminator, parsed.factDiscriminator);

            MemoryRecordIdentity otherOwner = new MemoryRecordIdentity
            {
                ownerPawnId = "Pawn_B",
                ownerEpochToken = record.ownerEpochToken,
                sourceOccurrenceId = record.sourceOccurrenceId,
                captureRuleId = record.captureRuleId,
                factDiscriminator = record.factDiscriminator
            };
            string otherRecordId;
            AssertTrue("record.private-owner.create",
                MemoryIdentityCodec.TryCreateRecordId(otherOwner, out otherRecordId));
            AssertTrue("record.private-owner.distinct", recordId != otherRecordId);

            string secondFact;
            record.factDiscriminator = "parent";
            AssertTrue("record.fact-discriminator.create",
                MemoryIdentityCodec.TryCreateRecordId(record, out secondFact));
            AssertTrue("record.fact-discriminator.distinct", recordId != secondFact);

            MemoryRootIdentity root = Root("Pawn_A", Epoch(1), "pawn", "Pawn_B");
            string rootId;
            MemoryIdentityCodec.TryCreateRootId(root, out rootId);
            string chapterId;
            AssertTrue("chapter.create",
                MemoryIdentityCodec.TryCreateChapterId(rootId, 12, out chapterId));
            AssertEqual(
                "chapter.golden",
                "17:memory-chapter-v163:14:memory-root-v16:Pawn_A21:15:memory-epoch-v11:14:pawn6:Pawn_B2:12",
                chapterId);
            string parsedRoot;
            long ordinal;
            AssertTrue("chapter.parse",
                MemoryIdentityCodec.TryParseChapterId(chapterId, out parsedRoot, out ordinal));
            AssertEqual("chapter.parse.root", rootId, parsedRoot);
            AssertEqual("chapter.parse.ordinal", 12L, ordinal);
            AssertTrue("chapter.negative.reject",
                !MemoryIdentityCodec.TryCreateChapterId(rootId, -1, out chapterId));
            AssertTrue("chapter.zero.reject",
                !MemoryIdentityCodec.TryCreateChapterId(rootId, 0, out chapterId));
            string oversizedEmbeddedRoot;
            AssertTrue("chapter.oversized-root-fixture",
                MemoryIdentityCodec.TryCreateRootId(
                    Root("Pawn_A", Epoch(1), "pawn",
                        new string('s', MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters)),
                    out oversizedEmbeddedRoot)
                && oversizedEmbeddedRoot.Length
                    > MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters);
            AssertTrue("chapter.oversized-embedded-root.reject",
                !MemoryIdentityCodec.TryCreateChapterId(oversizedEmbeddedRoot, 1, out chapterId));
            AssertTrue("chapter.noncanonical-number.reject",
                !MemoryIdentityCodec.TryParseChapterId(
                    OrdinalSegmentCodec.Segment("memory-chapter-v1")
                    + OrdinalSegmentCodec.Segment(rootId)
                    + OrdinalSegmentCodec.Segment("01"),
                    out parsedRoot,
                    out ordinal));
        }

        private static void TestFactionAndSummaryIdentity()
        {
            string faction;
            AssertTrue("faction.create",
                MemoryIdentityCodec.TryCreateFactionSubjectId("Faction_17", 3, out faction));
            AssertEqual(
                "faction.golden",
                "25:memory-faction-subject-v110:Faction_171:3",
                faction);
            string parsedFaction;
            long parsedGeneration;
            AssertTrue("faction.parse", MemoryIdentityCodec.TryParseFactionSubjectId(
                faction, out parsedFaction, out parsedGeneration));
            AssertEqual("faction.parse.id", "Faction_17", parsedFaction);
            AssertEqual("faction.parse.generation", 3L, parsedGeneration);
            AssertTrue("faction.parse.empty-instance.reject",
                !MemoryIdentityCodec.TryParseFactionSubjectId(
                    "25:memory-faction-subject-v10:1:3",
                    out parsedFaction,
                    out parsedGeneration));

            MemoryRootIdentity root = Root("Pawn_A", Epoch(1), "faction", faction);
            string rolling;
            string closed;
            AssertTrue("summary.rolling.create",
                MemoryIdentityCodec.TryCreateRollingSummaryId(root, out rolling));
            AssertTrue("summary.closed.create",
                MemoryIdentityCodec.TryCreateClosedSummaryId(root, 7, out closed));
            AssertTrue("summary.roles.distinct", rolling != closed);
            AssertContains("summary.rolling.domain", rolling, "memory-summary-rolling-v1");
            AssertContains("summary.closed.domain", closed, "memory-summary-closed-v1");
            AssertTrue("summary.closed.negative",
                !MemoryIdentityCodec.TryCreateClosedSummaryId(root, -1, out closed));
            AssertTrue("summary.closed.zero",
                !MemoryIdentityCodec.TryCreateClosedSummaryId(root, 0, out closed));
        }

        private static void TestSourceOccurrenceFallback()
        {
            MemorySourceOccurrenceFallback input = new MemorySourceOccurrenceFallback
            {
                stableSignalToken = "death.family",
                eventTickInvariant = 123456,
                sourceLocalSequenceInvariant = 2,
                factDiscriminator = "victim",
                sourceProvesUniqueness = true,
                subjects = new List<MemoryTypedSubject>
                {
                    Subject("pawn", "Pawn_B"),
                    Subject("faction", Faction("Faction_2", 1)),
                    Subject("pawn", "Pawn_B")
                }
            };
            string first;
            AssertTrue("sourceFallback.create",
                MemoryIdentityCodec.TryCreateSourceOccurrenceFallback(input, out first));
            AssertEqual(
                "sourceFallback.golden",
                "36:memory-source-occurrence-fallback-v112:death.family6:1234561:26:victim1:27:faction42:25:memory-faction-subject-v19:Faction_21:14:pawn6:Pawn_B",
                first);

            input.subjects.Reverse();
            string permuted;
            AssertTrue("sourceFallback.permutation.create",
                MemoryIdentityCodec.TryCreateSourceOccurrenceFallback(input, out permuted));
            AssertEqual("sourceFallback.permutation", first, permuted);

            input.sourceLocalSequenceInvariant = 3;
            string second;
            AssertTrue("sourceFallback.sequence.create",
                MemoryIdentityCodec.TryCreateSourceOccurrenceFallback(input, out second));
            AssertTrue("sourceFallback.sequence.distinct", first != second);

            input.sourceProvesUniqueness = false;
            AssertTrue("sourceFallback.unproved.reject",
                !MemoryIdentityCodec.TryCreateSourceOccurrenceFallback(input, out second));
            input.sourceProvesUniqueness = true;
            input.eventTickInvariant = -1;
            AssertTrue("sourceFallback.negative-tick.reject",
                !MemoryIdentityCodec.TryCreateSourceOccurrenceFallback(input, out second));
        }

        private static void TestEpochAllocationIdentity()
        {
            MemoryEpochAllocationPlan normal = MemoryIdentityCodec.PlanEpochAllocation(
                new MemoryEpochAllocationRequest
                {
                    ownerPawnId = "Pawn_A",
                    lastIssuedSequence = 0
                });
            AssertTrue("epoch.normal.mutates", normal.canMutate);
            AssertEqual("epoch.normal.outcome", MemoryEpochAllocationPlan.Normal, normal.outcomeToken);
            AssertEqual("epoch.normal.golden", "15:memory-epoch-v11:1", normal.epochToken);
            AssertEqual("epoch.normal.sequence", 1L, normal.nextSequence);
            AssertEqual("epoch.normal.chain-empty", string.Empty, normal.nextFallbackChain);

            bool isFallback;
            AssertTrue("epoch.normal.valid",
                MemoryIdentityCodec.TryValidateEpochToken(normal.epochToken, out isFallback));
            AssertTrue("epoch.normal.not-fallback", !isFallback);
            AssertTrue("epoch.normal.leading-zero.reject",
                !MemoryIdentityCodec.TryValidateEpochToken(
                    "15:memory-epoch-v12:01", out isFallback));
            AssertTrue("epoch.normal.zero.reject",
                !MemoryIdentityCodec.TryValidateEpochToken(
                    "15:memory-epoch-v11:0", out isFallback));

            MemoryEpochAllocationRequest saturated = new MemoryEpochAllocationRequest
            {
                ownerPawnId = "Pawn_A",
                lastIssuedSequence = long.MaxValue
            };
            MemoryEpochAllocationPlan firstFallback =
                MemoryIdentityCodec.PlanEpochAllocation(saturated);
            MemoryEpochAllocationPlan byteEquivalentRetry =
                MemoryIdentityCodec.PlanEpochAllocation(saturated);
            AssertTrue("epoch.fallback.mutates", firstFallback.canMutate);
            AssertEqual("epoch.fallback.outcome", MemoryEpochAllocationPlan.Fallback,
                firstFallback.outcomeToken);
            AssertEqual("epoch.fallback.probe-zero", 0L, firstFallback.probeOrdinal);
            AssertEqual("epoch.fallback.seed.golden",
                "684ca9291fc1c7a1241f679483a5c82f3340e8cbabe0f9f9a7e0a56299014168",
                firstFallback.priorFallbackChain);
            AssertEqual("epoch.fallback.step.golden",
                "9479af77bcb20b1aac1870cedc6151baaad3ed44f56af9b2408bd9de70fd9d1d",
                firstFallback.stepHash);
            AssertEqual("epoch.fallback.candidate.golden",
                "24:memory-epoch-fallback-v164:9479af77bcb20b1aac1870cedc6151baaad3ed44f56af9b2408bd9de70fd9d1d1:0",
                firstFallback.epochToken);
            AssertEqual("epoch.fallback.commit.golden",
                "8e9a77e6acebdc8a80175d6fee1d7b9466661c1e32d00b72efa54ee58902a982",
                firstFallback.nextFallbackChain);
            AssertEqual("epoch.fallback.retry.token", firstFallback.epochToken,
                byteEquivalentRetry.epochToken);
            AssertEqual("epoch.fallback.retry.chain", firstFallback.nextFallbackChain,
                byteEquivalentRetry.nextFallbackChain);
            AssertTrue("epoch.fallback.valid",
                MemoryIdentityCodec.TryValidateEpochToken(firstFallback.epochToken, out isFallback)
                && isFallback);

            MemoryEpochAllocationRequest collision = new MemoryEpochAllocationRequest
            {
                ownerPawnId = "Pawn_A",
                lastIssuedSequence = long.MaxValue,
                fallbackChain = new string('0', 64),
                liveEpochCarriers = new List<string>()
            };
            MemoryEpochAllocationPlan probeZero = MemoryIdentityCodec.PlanEpochAllocation(collision);
            collision.liveEpochCarriers.Add(probeZero.epochToken);
            collision.liveEpochCarriers.Add("malformed inert carrier");
            MemoryEpochAllocationPlan probeOne = MemoryIdentityCodec.PlanEpochAllocation(collision);
            AssertEqual("epoch.fallback.collision.probe-one", 1L, probeOne.probeOrdinal);
            AssertTrue("epoch.fallback.collision.distinct", probeZero.epochToken != probeOne.epochToken);

            MemoryEpochAllocationRequest malformed = new MemoryEpochAllocationRequest
            {
                ownerPawnId = "Pawn_A",
                lastIssuedSequence = long.MaxValue,
                fallbackChain = "ABC",
                liveEpochCarriers = new List<string> { firstFallback.epochToken }
            };
            MemoryEpochAllocationPlan ordinaryRefusal = MemoryIdentityCodec.PlanEpochAllocation(malformed);
            AssertTrue("epoch.invalid.ordinary-refuses", !ordinaryRefusal.canMutate);
            malformed.isTargetBrainwipe = true;
            MemoryEpochAllocationPlan repaired = MemoryIdentityCodec.PlanEpochAllocation(malformed);
            AssertTrue("epoch.invalid.brainwipe-repairs", repaired.canMutate);
            AssertTrue("epoch.invalid.brainwipe-flag", repaired.repairedFallbackChain);
            AssertEqual("epoch.invalid.brainwipe-repair-cursor.golden",
                "3aa6d9bd1a6ac9a4ab382ccae2a2b1c088b68434678143a31bae182e34af5332",
                repaired.priorFallbackChain);
            malformed.liveEpochCarriers.Reverse();
            MemoryEpochAllocationPlan repairedPermutation =
                MemoryIdentityCodec.PlanEpochAllocation(malformed);
            AssertEqual("epoch.repair.permutation.token", repaired.epochToken,
                repairedPermutation.epochToken);
            AssertEqual("epoch.repair.permutation.chain", repaired.nextFallbackChain,
                repairedPermutation.nextFallbackChain);

            MemoryEpochAllocationRequest emptyChainWithFallback = new MemoryEpochAllocationRequest
            {
                ownerPawnId = "Pawn_B",
                lastIssuedSequence = 7,
                liveEpochCarriers = new List<string> { firstFallback.epochToken }
            };
            AssertTrue("epoch.empty-chain.live-fallback.refuses",
                !MemoryIdentityCodec.PlanEpochAllocation(emptyChainWithFallback).canMutate);
            emptyChainWithFallback.isTargetBrainwipe = true;
            AssertTrue("epoch.empty-chain.live-fallback.brainwipe",
                MemoryIdentityCodec.PlanEpochAllocation(emptyChainWithFallback).canMutate);

            MemoryEpochAllocationPlan corruptLowWithValidChain =
                MemoryIdentityCodec.PlanEpochAllocation(new MemoryEpochAllocationRequest
                {
                    ownerPawnId = "Pawn_A",
                    lastIssuedSequence = 7,
                    fallbackChain = new string('0', 64)
                });
            AssertTrue("epoch.valid-chain.corrupt-low.mutates",
                corruptLowWithValidChain.canMutate);
            AssertEqual("epoch.valid-chain.raises-high-water",
                long.MaxValue,
                corruptLowWithValidChain.nextSequence);
        }

        private static void TestSyntheticAndRepairIdentity()
        {
            MemoryRootIdentity root = Root("Pawn_A", Epoch(1), "pawn", "Pawn_B");
            string rollingSource;
            string closedSource;
            AssertTrue("synthetic.source.rolling",
                MemoryIdentityCodec.TryCreateRollingSummarySourceId(root, out rollingSource));
            AssertTrue("synthetic.source.closed",
                MemoryIdentityCodec.TryCreateClosedSummarySourceId(root, 3, out closedSource));
            AssertContains("synthetic.source.domain", rollingSource, "memory-summary-source-v1");
            AssertTrue("synthetic.source.roles-distinct", rollingSource != closedSource);

            string factId;
            AssertTrue("fact-id.create", MemoryIdentityCodec.TryCreateFactId(
                "rule.birth", "child", "birth", "pawn", "Pawn_B", "count_occurrences",
                out factId));
            AssertEqual("fact-id.golden",
                "10:rule.birth5:child5:birth4:pawn6:Pawn_B17:count_occurrences", factId);

            string subjectRefId;
            AssertTrue("subject-ref.create", MemoryIdentityCodec.TryCreateSubjectRefId(
                "pawn", "Pawn_B", "child", "known", out subjectRefId));
            AssertEqual("subject-ref.golden", "4:pawn6:Pawn_B5:child5:known", subjectRefId);

            string provenance;
            AssertTrue("provenance.capture.create", MemoryIdentityCodec.TryCreateProvenanceRefId(
                "capture_signal", "signal:42", string.Empty, "rule.birth", "child",
                string.Empty, out provenance));
            AssertContains("provenance.domain", provenance, "memory-provenance-ref-v1");
            AssertTrue("provenance.diary.missing-event.reject",
                !MemoryIdentityCodec.TryCreateProvenanceRefId(
                    "diary_event", "signal:42", string.Empty, "rule.birth", "child",
                    string.Empty, out provenance));
            AssertTrue("provenance.non-integration.token.reject",
                !MemoryIdentityCodec.TryCreateProvenanceRefId(
                    "capture_signal", "signal:42", string.Empty, "rule.birth", "child",
                    "bridge", out provenance));

            string contribution;
            AssertTrue("contribution.create", MemoryIdentityCodec.TryCreateContributionId(
                "record:42", 0, factId, out contribution));
            AssertContains("contribution.domain", contribution, "memory-contribution-v1");
            AssertTrue("contribution.negative.reject",
                !MemoryIdentityCodec.TryCreateContributionId("record:42", -1, factId,
                    out contribution));

            string identityTuple = OrdinalSegmentCodec.Segment("identity")
                + OrdinalSegmentCodec.Segment("Pawn_A");
            string payloadTuple = OrdinalSegmentCodec.Segment("payload")
                + OrdinalSegmentCodec.Segment("value");
            string firstRepair = string.Empty;
            foreach (string kind in new[] { "record", "contribution", "chapter", "archive" })
            {
                string repair;
                AssertTrue("repair.create." + kind, MemoryIdentityCodec.TryCreateRepairId(
                    kind, "opaque:7", identityTuple, payloadTuple, 0, out repair));
                MemoryRepairIdentity parsed;
                AssertTrue("repair.parse." + kind,
                    MemoryIdentityCodec.TryParseRepairId(repair, out parsed));
                AssertEqual("repair.parse.kind." + kind, kind, parsed.kindToken);
                AssertEqual("repair.parse.ordinal." + kind, 0L, parsed.collisionOrdinal);
                if (firstRepair.Length == 0) firstRepair = repair;
                else AssertTrue("repair.kind.distinct." + kind, firstRepair != repair);
            }
            AssertEqual("repair.record.golden",
                "19:memory-repair-id-v16:record8:opaque:764:5608fdac3a868b16ae9faeee7cad94ae9b99857ed56ff214be9fabace123019464:e9fc6b8765aa2063f3538c814922794e0b8d8f3f248beaa3239d0c82597322c91:0",
                firstRepair);
            AssertTrue("repair.unknown-kind.reject", !MemoryIdentityCodec.TryCreateRepairId(
                "summary", "opaque:7", identityTuple, payloadTuple, 0, out firstRepair));
            AssertTrue("repair.negative.reject", !MemoryIdentityCodec.TryCreateRepairId(
                "record", "opaque:7", identityTuple, payloadTuple, -1, out firstRepair));
            AssertTrue("repair.unframed-identity.reject", !MemoryIdentityCodec.TryCreateRepairId(
                "record", "opaque:7", "identity", payloadTuple, 0, out firstRepair));
            AssertTrue("repair.noncanonical-tuple.reject", !MemoryIdentityCodec.TryCreateRepairId(
                "record", "opaque:7", "08:identity", payloadTuple, 0, out firstRepair));

            string framedHash;
            AssertTrue("hash.H.create", MemoryIdentityCodec.TryComputeFramedHash(
                "memory-manifest-entry-v1", new[] { "releaseCandidate", "vector", "policy" },
                out framedHash));
            AssertEqual("hash.H.length", 64, framedHash.Length);
            AssertTrue("hash.H.lowercase", framedHash.All(value =>
                (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f')));
        }

        private static void TestRequestIdentity()
        {
            string logicalRequestId;
            AssertTrue("request-id.create",
                MemoryIdentityCodec.TryCreateLogicalRequestId(42, out logicalRequestId));
            AssertEqual("request-id.golden", "25:memory-logical-request-v12:42", logicalRequestId);
            long parsedSequence;
            AssertTrue("request-id.parse",
                MemoryIdentityCodec.TryParseLogicalRequestId(logicalRequestId, out parsedSequence));
            AssertEqual("request-id.sequence", 42L, parsedSequence);
            AssertTrue("request-id.zero.reject",
                !MemoryIdentityCodec.TryCreateLogicalRequestId(0, out logicalRequestId));
            AssertTrue("request-id.leading-zero.reject",
                !MemoryIdentityCodec.TryParseLogicalRequestId(
                    "25:memory-logical-request-v12:01", out parsedSequence));
            MemoryIdentityCodec.TryCreateLogicalRequestId(42, out logicalRequestId);

            List<MemoryEvidenceIdentity> evidence = new List<MemoryEvidenceIdentity>
            {
                new MemoryEvidenceIdentity
                {
                    recordId = "r1", sourceOccurrenceId = "s1", rootIdOrEmpty = "root1"
                },
                new MemoryEvidenceIdentity
                {
                    recordId = "r2", sourceOccurrenceId = "s2", rootIdOrEmpty = string.Empty
                }
            };
            string evidenceFingerprint;
            AssertTrue("evidence-fingerprint.create",
                MemoryIdentityCodec.TryCreateEvidenceSetFingerprint(
                    evidence, out evidenceFingerprint));
            AssertEqual("evidence-fingerprint.golden",
                "8500d2d001c5f7e8aee6ef14366a98e2d84c3fbe985fcb71c87ce04d6828c895",
                evidenceFingerprint);
            evidence.Reverse();
            string reversedEvidence;
            AssertTrue("evidence-fingerprint.reverse.create",
                MemoryIdentityCodec.TryCreateEvidenceSetFingerprint(
                    evidence, out reversedEvidence));
            AssertTrue("evidence-fingerprint.order-significant",
                evidenceFingerprint != reversedEvidence);
            evidence.Reverse();
            evidence.Add(evidence[0]);
            AssertTrue("evidence-fingerprint.duplicate.reject",
                !MemoryIdentityCodec.TryCreateEvidenceSetFingerprint(
                    evidence, out reversedEvidence));
            evidence.RemoveAt(evidence.Count - 1);

            List<MemoryGuardIdentity> guards = new List<MemoryGuardIdentity>
            {
                new MemoryGuardIdentity { guardKind = "record", guardKey = "r1" },
                new MemoryGuardIdentity { guardKind = "root", guardKey = "root1" }
            };
            string receiptFingerprint;
            AssertTrue("receipt-fingerprint.create",
                MemoryIdentityCodec.TryCreateReceiptPlanFingerprint(
                    evidence, guards, out receiptFingerprint));
            AssertEqual("receipt-fingerprint.golden",
                "2ab3877e3b7efe4061a03e41aa342cba82dea0f5ade1eb307abae8e96f53b3fe",
                receiptFingerprint);
            guards.Reverse();
            AssertTrue("receipt-fingerprint.unsorted-guards.reject",
                !MemoryIdentityCodec.TryCreateReceiptPlanFingerprint(
                    evidence, guards, out receiptFingerprint));
            guards.Reverse();

            string evidenceEpochToken;
            AssertTrue("evidence-epoch.create", MemoryIdentityCodec.TryCreateEvidenceEpochToken(
                "normal_diary", "event:42", "initiator", "Pawn_A", Epoch(1),
                evidence, guards, out evidenceEpochToken));
            AssertEqual("evidence-epoch.golden",
                "695c6ff294b67f9fade527a93ed02e34d2752602cb91f0b42701358918329120",
                evidenceEpochToken);
            evidence.Reverse();
            guards.Reverse();
            evidence.Add(evidence[0]);
            string permutedEvidenceEpoch;
            AssertTrue("evidence-epoch.permutation.create",
                MemoryIdentityCodec.TryCreateEvidenceEpochToken(
                    "normal_diary", "event:42", "initiator", "Pawn_A", Epoch(1),
                    evidence, guards, out permutedEvidenceEpoch));
            AssertEqual("evidence-epoch.permutation-dedup", evidenceEpochToken,
                permutedEvidenceEpoch);
            evidence.RemoveAt(evidence.Count - 1);
            evidence.Reverse();
            guards.Reverse();

            List<MemoryDiagnosticIdentity> diagnostics = new List<MemoryDiagnosticIdentity>
            {
                new MemoryDiagnosticIdentity
                {
                    provenanceKindToken = "memory", sourceId = "line-source",
                    recordIdOrEmpty = "r1", sourceOccurrenceIdOrEmpty = "s1",
                    rootIdOrEmpty = "root1", lineOrdinal = 0
                },
                new MemoryDiagnosticIdentity
                {
                    provenanceKindToken = "transport", sourceId = "line-source-2",
                    lineOrdinal = 1
                }
            };
            string diagnosticFingerprint;
            AssertTrue("diagnostic-fingerprint.create",
                MemoryIdentityCodec.TryCreateDiagnosticProvenanceFingerprint(
                    diagnostics, out diagnosticFingerprint));
            AssertEqual("diagnostic-fingerprint.golden",
                "28a988be01cc30e06b24c3da186eb5e232fa540ad21cc517ed865814ad265b05",
                diagnosticFingerprint);
            diagnostics.Reverse();
            AssertTrue("diagnostic-fingerprint.unsorted.reject",
                !MemoryIdentityCodec.TryCreateDiagnosticProvenanceFingerprint(
                    diagnostics, out diagnosticFingerprint));
            diagnostics.Reverse();

            string requestKey;
            AssertTrue("request-key.create", MemoryIdentityCodec.TryCreateLogicalRequestKey(
                "normal_diary", "event:42", "initiator", "Pawn_A", Epoch(1),
                new string('a', 64), out requestKey));
            AssertEqual("request-key.golden",
                "63ac2c754c1018f861768d786e56a5b4e157376f9bf82e882143b7de061bd872",
                requestKey);
            AssertTrue("request-key.owner-pair.reject",
                !MemoryIdentityCodec.TryCreateLogicalRequestKey(
                    "normal_diary", "event:42", "initiator", "Pawn_A", string.Empty,
                    new string('a', 64), out requestKey));
            AssertTrue("request-key.neutral-ownerless", MemoryIdentityCodec.TryCreateLogicalRequestKey(
                "manual_regenerate", "event:42", "neutral", string.Empty, string.Empty,
                new string('a', 64), out requestKey));

            MemoryIdentityCodec.TryCreateReceiptPlanFingerprint(
                evidence, guards, out receiptFingerprint);
            MemoryIdentityCodec.TryCreateDiagnosticProvenanceFingerprint(
                diagnostics, out diagnosticFingerprint);
            string variantKey;
            AssertTrue("variant-key.create", MemoryIdentityCodec.TryCreatePromptVariantKey(
                logicalRequestId, 0, "normal_diary", "template:v1", "detail:v1",
                "system", "user", receiptFingerprint, diagnosticFingerprint, out variantKey));
            AssertEqual("variant-key.golden",
                "1fc7201548edd3c9495f87697b83addeb4923d35ec2909e5acae71c183fdd281",
                variantKey);
            AssertTrue("variant-key.prompt-distinguishes",
                MemoryIdentityCodec.TryCreatePromptVariantKey(
                    logicalRequestId, 0, "normal_diary", "template:v1", "detail:v1",
                    "system", "user!", receiptFingerprint, diagnosticFingerprint, out requestKey)
                && requestKey != variantKey);
            AssertTrue("variant-key.unpaired-prompt.reject",
                !MemoryIdentityCodec.TryCreatePromptVariantKey(
                    logicalRequestId, 0, "normal_diary", "template:v1", "detail:v1",
                    "\uD800", "user", receiptFingerprint, diagnosticFingerprint, out requestKey));

            AssertTrue("permit.request-key.create", MemoryIdentityCodec.TryCreateLogicalRequestKey(
                "normal_diary", "event:42", "initiator", "Pawn_A", Epoch(1),
                evidenceEpochToken, out requestKey));
            AssertEqual("permit.request-key.golden",
                "3e96c003504c36eda4c90c88b8f5bc9c883337d6f3898334804bae72fc62d7b2",
                requestKey);
            MemoryInvocationPermitIdentity permit = new MemoryInvocationPermitIdentity
            {
                logicalRequestId = logicalRequestId,
                logicalRequestKey = requestKey,
                requestPurposeToken = "normal_diary",
                sessionId = 7,
                eventIdOrOpportunityKey = "event:42",
                povRoleToken = "initiator",
                ownerPawnId = "Pawn_A",
                ownerEpochToken = Epoch(1),
                evidenceEpochToken = evidenceEpochToken,
                ownerCancellationGeneration = 1,
                globalCancellationGeneration = 1,
                optionalRequestInvalidationGeneration = 0,
                attemptOrdinal = 1,
                variantKey = variantKey,
                receiptPlanFingerprint = receiptFingerprint,
                invocationSequence = 9,
                invocationTick = 123,
                narrativeUseWinnerAttemptOrdinal = 1
            };
            string permitFingerprint;
            AssertTrue("permit.create", MemoryIdentityCodec.TryCreateInvocationPermitFingerprint(
                permit, out permitFingerprint));
            AssertEqual("permit.golden",
                "48092ede40a1ad20c6ec6d44c5afacac3f4c2f9da52576dfb3bbd86bb022815b",
                permitFingerprint);
            permit.logicalRequestKey = new string('b', 64);
            AssertTrue("permit.request-key-mismatch.reject",
                !MemoryIdentityCodec.TryCreateInvocationPermitFingerprint(
                    permit, out requestKey));
            permit.logicalRequestKey = "3e96c003504c36eda4c90c88b8f5bc9c883337d6f3898334804bae72fc62d7b2";
            permit.invocationTick++;
            AssertTrue("permit.single-field.create",
                MemoryIdentityCodec.TryCreateInvocationPermitFingerprint(
                    permit, out requestKey));
            AssertTrue("permit.single-field-distinguishes", requestKey != permitFingerprint);
            permit.invocationTick--;
            permit.invocationSequence = 0;
            AssertTrue("permit.pre-invocation.reject",
                !MemoryIdentityCodec.TryCreateInvocationPermitFingerprint(
                    permit, out requestKey));
        }

        private static void TestMemoryDispatchPolicy()
        {
            MemoryLogicalRequestSnapshot request = BuildDispatchRequest();
            AssertTrue("dispatch.valid", MemoryDispatchPolicy.ValidateRequest(request));

            request.reservedEvidence.Reverse();
            AssertTrue("dispatch.reservation-order.reject",
                !MemoryDispatchPolicy.ValidateRequest(request));
            request.reservedEvidence.Reverse();

            MemoryLogicalAttemptSnapshot first;
            AssertTrue("dispatch.attempt.initial.plan",
                MemoryDispatchPolicy.TryPlanPreparedAttempt(
                    request,
                    request.variants[0].variantKey,
                    MemoryDispatchTokens.Initial,
                    0,
                    out first));
            request.attempts.Add(first);
            request.lastIssuedAttemptOrdinal = 1;
            AssertTrue("dispatch.prepared.valid", MemoryDispatchPolicy.ValidateRequest(request));

            MemoryDispatchFenceSnapshot fence = DispatchFence(request);
            MemoryInvocationCommitPlan invocation = MemoryDispatchPolicy.PlanInvocationCommit(
                request,
                1,
                fence,
                0,
                600);
            AssertTrue("dispatch.invocation.can-commit", invocation.canCommit);
            AssertEqual("dispatch.invocation.sequence", 1L, invocation.nextInvocationSequence);
            AssertTrue("dispatch.invocation.potential", invocation.applyPotentialExposure);
            AssertTrue("dispatch.invocation.narrative", invocation.applyNarrativeUse);
            AssertEqual("dispatch.invocation.winner", 1,
                invocation.narrativeUseWinnerAttemptOrdinal);
            AssertTrue("dispatch.permit.fingerprint",
                MemoryDispatchPolicy.PermitFingerprintIsValid(invocation.permit));

            first.attemptStateToken =
                MemoryRequestStateMachineContracts.AttemptInvocationCommitted;
            first.invocationSequence = invocation.nextInvocationSequence;
            first.invocationTick = invocation.permit.invocationTick;
            first.potentialExposureApplied = invocation.applyPotentialExposure;
            first.narrativeUseApplied = invocation.applyNarrativeUse;
            request.requestStateToken = MemoryRequestStateMachineContracts.InvocationCommitted;
            request.narrativeUseWinnerAttemptOrdinal =
                invocation.narrativeUseWinnerAttemptOrdinal;
            request.narrativeUseWinnerVariantKey =
                invocation.narrativeUseWinnerVariantKey;
            AssertTrue("dispatch.committed.valid", MemoryDispatchPolicy.ValidateRequest(request));

            MemoryTerminalCallbackPlan terminal = MemoryDispatchPolicy.PlanTerminalCallback(
                request,
                invocation.permit,
                fence,
                MemoryDispatchTokens.Success,
                true);
            AssertTrue("dispatch.terminal.accepted", terminal.accepted);
            AssertTrue("dispatch.terminal.confirmed", terminal.applyConfirmedExposure);
            AssertTrue("dispatch.terminal.result", terminal.applyResult);
            AssertEqual("dispatch.terminal.receipt-first",
                "confirmed_exposure_receipt", terminal.orderedOperations[0]);
            AssertEqual("dispatch.terminal.result-second",
                "result_publication", terminal.orderedOperations[1]);

            first.attemptStateToken =
                MemoryRequestStateMachineContracts.AttemptReceiptApplied;
            first.terminalTick = 650;
            first.terminalOutcomeToken = MemoryDispatchTokens.Success;
            AssertTrue("dispatch.receipt-state.valid",
                MemoryDispatchPolicy.ValidateRequest(request));
            first.resultApplied = true;
            first.attemptStateToken =
                MemoryRequestStateMachineContracts.AttemptTerminalPending;
            AssertTrue("dispatch.terminal-state.valid",
                MemoryDispatchPolicy.ValidateRequest(request));
            MemoryTerminalCallbackPlan duplicate = MemoryDispatchPolicy.PlanTerminalCallback(
                request,
                invocation.permit,
                fence,
                MemoryDispatchTokens.Success,
                true);
            AssertTrue("dispatch.terminal.duplicate", duplicate.duplicate);
            AssertTrue("dispatch.terminal.duplicate.no-result", !duplicate.applyResult);
            MemoryTerminalCallbackPlan relabeledDuplicate =
                MemoryDispatchPolicy.PlanTerminalCallback(
                    request,
                    invocation.permit,
                    fence,
                    MemoryDispatchTokens.ProviderError,
                    false);
            AssertTrue("dispatch.terminal.duplicate-outcome-change.reject",
                !relabeledDuplicate.accepted && !relabeledDuplicate.duplicate);
            first.resultApplied = false;
            first.attemptStateToken =
                MemoryRequestStateMachineContracts.AttemptInvocationCommitted;
            first.terminalTick = 0;
            first.terminalOutcomeToken = string.Empty;

            first.attemptStateToken =
                MemoryRequestStateMachineContracts.AttemptReceiptApplied;
            first.terminalTick = 651;
            first.terminalOutcomeToken = string.Empty;
            AssertTrue("dispatch.receipt-missing-outcome.reject",
                !MemoryDispatchPolicy.ValidateRequest(request));
            first.terminalOutcomeToken = "unknown-terminal";
            AssertTrue("dispatch.receipt-unknown-outcome.reject",
                !MemoryDispatchPolicy.ValidateRequest(request));
            first.attemptStateToken =
                MemoryRequestStateMachineContracts.AttemptInvocationCommitted;
            first.terminalTick = 0;
            first.terminalOutcomeToken = string.Empty;

            fence.ownerCancellationGeneration++;
            MemoryTerminalCallbackPlan stale = MemoryDispatchPolicy.PlanTerminalCallback(
                request,
                invocation.permit,
                fence,
                MemoryDispatchTokens.Success,
                true);
            AssertTrue("dispatch.terminal.old-epoch-reject", !stale.accepted);
            AssertEqual("dispatch.terminal.old-epoch-outcome",
                MemoryDispatchTokens.Stale, stale.outcomeToken);
            fence.ownerCancellationGeneration--;

            string originalPermitFingerprint = invocation.permit.permitFingerprint;
            invocation.permit.invocationTick++;
            AssertTrue("dispatch.permit.single-field-reject",
                !MemoryDispatchPolicy.PermitFingerprintIsValid(invocation.permit));
            invocation.permit.invocationTick--;
            invocation.permit.permitFingerprint = originalPermitFingerprint;

            MemoryRuntimeSendEnvelope envelope = new MemoryRuntimeSendEnvelope(invocation.permit);
            AssertTrue("dispatch.send-claim.first", envelope.TryClaimPhysicalSend());
            AssertTrue("dispatch.send-claim.duplicate-reject", !envelope.TryClaimPhysicalSend());

            first.attemptStateToken = MemoryRequestStateMachineContracts.AttemptTerminalPending;
            first.terminalTick = 652;
            first.terminalOutcomeToken = MemoryDispatchTokens.Success;
            first.resultApplied = true;
            MemoryLogicalAttemptSnapshot failover;
            AssertTrue("dispatch.attempt.failover.plan",
                MemoryDispatchPolicy.TryPlanPreparedAttempt(
                    request,
                    request.variants[1].variantKey,
                    MemoryDispatchTokens.Failover,
                    1,
                    out failover));
            request.attempts.Add(failover);
            request.lastIssuedAttemptOrdinal = 2;
            MemoryInvocationCommitPlan failoverInvocation =
                MemoryDispatchPolicy.PlanInvocationCommit(request, 2, fence, 1, 601);
            AssertTrue("dispatch.failover.can-commit", failoverInvocation.canCommit);
            AssertEqual("dispatch.failover.sequence", 2L,
                failoverInvocation.nextInvocationSequence);
            AssertEqual("dispatch.failover.keeps-first-winner", 1,
                failoverInvocation.narrativeUseWinnerAttemptOrdinal);
            AssertTrue("dispatch.failover.no-second-narrative",
                !failoverInvocation.applyNarrativeUse);

            MemoryInvocationCommitPlan saturated = MemoryDispatchPolicy.PlanInvocationCommit(
                request,
                2,
                fence,
                long.MaxValue,
                602);
            AssertTrue("dispatch.invocation.saturation-refuses", !saturated.canCommit);
            AssertEqual("dispatch.invocation.saturation-outcome",
                MemoryDispatchTokens.SequenceSaturated, saturated.outcomeToken);

            MemoryLogicalRequestSnapshot beforeLoad = BuildDispatchRequest();
            MemoryLogicalAttemptSnapshot neverInvoked;
            MemoryDispatchPolicy.TryPlanPreparedAttempt(
                beforeLoad,
                beforeLoad.variants[0].variantKey,
                MemoryDispatchTokens.Initial,
                0,
                out neverInvoked);
            beforeLoad.attempts.Add(neverInvoked);
            beforeLoad.lastIssuedAttemptOrdinal = 1;
            MemoryLoadSettlementPlan beforeSettlement =
                MemoryDispatchPolicy.PlanLoadedRequestSettlement(beforeLoad);
            AssertTrue("dispatch.load.before.valid", beforeSettlement.valid);
            AssertTrue("dispatch.load.before.no-exposure",
                !beforeSettlement.hadCommittedInvocation
                && beforeSettlement.potentialExposureAttemptOrdinals.Count == 0);
            AssertTrue("dispatch.load.before.retryable",
                beforeSettlement.restoreNormalPovRetryable);

            MemoryLogicalRequestSnapshot afterLoad = BuildDispatchRequest();
            MemoryLogicalAttemptSnapshot invoked;
            MemoryDispatchPolicy.TryPlanPreparedAttempt(
                afterLoad,
                afterLoad.variants[0].variantKey,
                MemoryDispatchTokens.Initial,
                0,
                out invoked);
            invoked.attemptStateToken =
                MemoryRequestStateMachineContracts.AttemptInvocationCommitted;
            invoked.invocationSequence = 3;
            invoked.invocationTick = 700;
            afterLoad.attempts.Add(invoked);
            afterLoad.lastIssuedAttemptOrdinal = 1;
            afterLoad.requestStateToken = MemoryRequestStateMachineContracts.InvocationCommitted;
            MemoryLoadSettlementPlan afterSettlement =
                MemoryDispatchPolicy.PlanLoadedRequestSettlement(afterLoad);
            AssertTrue("dispatch.load.after.valid", afterSettlement.valid);
            AssertTrue("dispatch.load.after.exposed", afterSettlement.hadCommittedInvocation);
            AssertEqual("dispatch.load.after.receipt-count", 1,
                afterSettlement.potentialExposureAttemptOrdinals.Count);
            AssertEqual("dispatch.load.after.earliest-winner", 1,
                afterSettlement.repairedNarrativeUseWinnerAttemptOrdinal);
            AssertTrue("dispatch.load.after.never-retry",
                !afterSettlement.restoreNormalPovRetryable);
        }

        private static void TestOptionalMemoryAiPolicy()
        {
            AssertTrue("optional.meaningful.pre-enable-never-catches-up",
                !MemoryOptionalAiPolicy.IsMeaningfulEventAfterEligibilityBaseline(99, 100));
            AssertTrue("optional.meaningful.exact-enable-tick-is-ambiguous-and-skipped",
                !MemoryOptionalAiPolicy.IsMeaningfulEventAfterEligibilityBaseline(100, 100));
            AssertTrue("optional.meaningful.first-later-tick-is-eligible",
                MemoryOptionalAiPolicy.IsMeaningfulEventAfterEligibilityBaseline(101, 100));
            AssertTrue("optional.meaningful.missing-baseline-fails-closed",
                !MemoryOptionalAiPolicy.IsMeaningfulEventAfterEligibilityBaseline(101, -1));
            AssertEqual("optional.meaningful.enable-baselines-now", 100L,
                MemoryOptionalAiPolicy.PlanMeaningfulEligibilityBaseline(
                    true, true, -1, 100));
            AssertEqual("optional.meaningful.reload-preserves-saved-baseline", 100L,
                MemoryOptionalAiPolicy.PlanMeaningfulEligibilityBaseline(
                    true, false, 100, 50000));
            AssertTrue("optional.meaningful.reload-keeps-delayed-event-eligible",
                MemoryOptionalAiPolicy.IsMeaningfulEventAfterEligibilityBaseline(
                    101,
                    MemoryOptionalAiPolicy.PlanMeaningfulEligibilityBaseline(
                        true, false, 100, 50000)));
            AssertEqual("optional.meaningful.old-save-baselines-current-truth", 50000L,
                MemoryOptionalAiPolicy.PlanMeaningfulEligibilityBaseline(
                    true, false, -1, 50000));
            AssertEqual("optional.meaningful.master-off-clears-boundary", -1L,
                MemoryOptionalAiPolicy.PlanMeaningfulEligibilityBaseline(
                    false, false, 100, 50000));
            AssertTrue("optional.policy.reconciled-generations-admit",
                MemoryOptionalAiPolicy.CanStageOptionalRequest(true, true, 3, 4));
            AssertTrue("optional.policy.unreconciled-fails-closed",
                !MemoryOptionalAiPolicy.CanStageOptionalRequest(false, true, 3, 4));
            AssertTrue("optional.policy.master-off-fails-closed",
                !MemoryOptionalAiPolicy.CanStageOptionalRequest(true, false, 3, 4));
            AssertTrue("optional.policy.saturated-generation-fails-closed",
                !MemoryOptionalAiPolicy.CanStageOptionalRequest(
                    true, true, long.MaxValue, 4));
            AssertTrue("optional.wake.pre-due-remains-wakeable",
                MemoryOptionalAiPolicy.IsBoundedOpportunityWakeable(100, 50, 25, 120));
            AssertTrue("optional.wake.last-pre-expiry-tick-remains-wakeable",
                MemoryOptionalAiPolicy.IsBoundedOpportunityWakeable(100, 50, 25, 174));
            AssertTrue("optional.wake.exact-expiry-stays-asleep",
                !MemoryOptionalAiPolicy.IsBoundedOpportunityWakeable(100, 50, 25, 175));
            AssertTrue("optional.wake.after-expiry-stays-asleep",
                !MemoryOptionalAiPolicy.IsBoundedOpportunityWakeable(100, 50, 25, 999));
            AssertTrue("optional.wake.saturated-due-stays-asleep",
                !MemoryOptionalAiPolicy.IsBoundedOpportunityWakeable(
                    long.MaxValue - 5, 10, 25, 999));
            string[] exactDispositions =
            {
                MemoryOptionalWordingDispositionTokens.None,
                MemoryOptionalWordingDispositionTokens.Pending,
                MemoryOptionalWordingDispositionTokens.Activated,
                MemoryOptionalWordingDispositionTokens.Success,
                MemoryOptionalWordingDispositionTokens.Failed,
                MemoryOptionalWordingDispositionTokens.Malformed,
                MemoryOptionalWordingDispositionTokens.Expired,
                MemoryOptionalWordingDispositionTokens.Displaced,
                MemoryOptionalWordingDispositionTokens.Disabled
            };
            for (int dispositionIndex = 0;
                dispositionIndex < exactDispositions.Length; dispositionIndex++)
                AssertTrue("optional.disposition.known." + dispositionIndex,
                    MemoryOptionalWordingDispositionTokens.IsKnown(
                        exactDispositions[dispositionIndex]));
            AssertTrue("optional.disposition.stale-is-not-saved-vocabulary",
                !MemoryOptionalWordingDispositionTokens.IsKnown("stale"));

            SummaryWordingOpportunitySnapshot first = SummaryOpportunity(
                "Pawn_Optional", Epoch(91), "root-a", "summary-a", 10, 10, 100);
            string key;
            AssertTrue("optional.summary-key.create",
                MemoryOptionalAiPolicy.TryCreateSummaryOpportunityKey(first, out key));
            first.opportunityKey = key;
            SummaryWordingOpportunitySnapshot parsed;
            AssertTrue("optional.summary-key.roundtrip",
                MemoryOptionalAiPolicy.TryParseSummaryOpportunityKey(key, out parsed));
            AssertEqual("optional.summary-key.owner", first.ownerPawnId, parsed.ownerPawnId);
            AssertEqual("optional.summary-key.fingerprint",
                first.projectionFingerprint, parsed.projectionFingerprint);
            AssertTrue("optional.summary-key.trailing-refused",
                !MemoryOptionalAiPolicy.TryParseSummaryOpportunityKey(
                    key + OrdinalSegmentCodec.Segment("extra"), out parsed));

            SummaryWordingOpportunitySnapshot lower = SummaryOpportunity(
                first.ownerPawnId, first.ownerEpochToken, "root-b", "summary-b", 10, 9, 110);
            MemoryOptionalAiPolicy.TryCreateSummaryOpportunityKey(lower, out lower.opportunityKey);
            SummaryWordingSlotPlan keepPriority = MemoryOptionalAiPolicy.PlanOwnerSlot(
                first, lower, 20);
            AssertTrue("optional.one-slot.valid", keepPriority.valid);
            AssertTrue("optional.one-slot.higher-priority-kept",
                ReferenceEquals(first, keepPriority.winner));
            AssertEqual("optional.one-slot.displaced-once", 1, keepPriority.terminal.Count);
            AssertEqual("optional.one-slot.disposition",
                MemoryOptionalWordingDispositionTokens.Displaced,
                keepPriority.terminal[0].dispositionToken);

            SummaryWordingOpportunitySnapshot newer = SummaryOpportunity(
                first.ownerPawnId, first.ownerEpochToken, "root-c", "summary-c", 10, 10, 120);
            MemoryOptionalAiPolicy.TryCreateSummaryOpportunityKey(newer, out newer.opportunityKey);
            SummaryWordingSlotPlan keepNewer = MemoryOptionalAiPolicy.PlanOwnerSlot(
                first, newer, 20);
            AssertTrue("optional.one-slot.newer-change-kept",
                ReferenceEquals(newer, keepNewer.winner));

            SummaryWordingOpportunitySnapshot expires = SummaryOpportunity(
                first.ownerPawnId, first.ownerEpochToken, "root-d", "summary-d", 1, 1, 10);
            MemoryOptionalAiPolicy.TryCreateSummaryOpportunityKey(expires, out expires.opportunityKey);
            SummaryWordingSlotPlan expired = MemoryOptionalAiPolicy.PlanOwnerSlot(
                expires, null, expires.expiryTick);
            AssertTrue("optional.expiry.exact-boundary",
                expired.valid && expired.winner == null && expired.terminal.Count == 1);
            AssertEqual("optional.expiry.terminal-token",
                MemoryOptionalWordingDispositionTokens.Expired,
                expired.terminal[0].dispositionToken);

            SummaryWordingCurrentSnapshot current = new SummaryWordingCurrentSnapshot
            {
                ownerPawnId = first.ownerPawnId,
                ownerEpochToken = first.ownerEpochToken,
                rootId = first.rootId,
                summaryRecordId = first.summaryRecordId,
                rootStructuralRevision = first.expectedRootStructuralRevision,
                summaryFactsRevision = first.expectedSummaryFactsRevision,
                reducerRevision = first.expectedReducerRevision,
                formatRevision = first.expectedFormatRevision,
                categoryMask = first.expectedCategoryMask,
                projectionFingerprint = first.projectionFingerprint,
                deterministicWording = "The deterministic wording remains truth."
            };
            SummaryWordingResultPlan success = MemoryOptionalAiPolicy.PlanSummaryResult(
                first, current, true, "  Concise optional wording.  ", 80);
            AssertTrue("optional.result.success-disposable-only",
                success.identityMatched && success.applyOptionalWording);
            AssertEqual("optional.result.trimmed", "Concise optional wording.",
                success.optionalWording);
            AssertEqual("optional.result.success-token",
                MemoryOptionalWordingDispositionTokens.Success, success.dispositionToken);
            AssertEqual("optional.cache.exact-projection-uses-disposable-wording",
                "Concise optional wording.",
                MemoryOptionalAiPolicy.SelectNaturalWritingWording(
                    current,
                    success.optionalWording,
                    current.projectionFingerprint,
                    current.formatRevision,
                    current.categoryMask,
                    MemoryOptionalWordingDispositionTokens.Success,
                    80));
            AssertEqual("optional.cache.fingerprint-mismatch-uses-deterministic",
                current.deterministicWording,
                MemoryOptionalAiPolicy.SelectNaturalWritingWording(
                    current,
                    success.optionalWording,
                    new string('b', 64),
                    current.formatRevision,
                    current.categoryMask,
                    MemoryOptionalWordingDispositionTokens.Success,
                    80));
            AssertEqual("optional.cache.mask-mismatch-uses-deterministic",
                current.deterministicWording,
                MemoryOptionalAiPolicy.SelectNaturalWritingWording(
                    current,
                    success.optionalWording,
                    current.projectionFingerprint,
                    current.formatRevision,
                    current.categoryMask ^ 1,
                    MemoryOptionalWordingDispositionTokens.Success,
                    80));
            AssertEqual("optional.cache.non-success-uses-deterministic",
                current.deterministicWording,
                MemoryOptionalAiPolicy.SelectNaturalWritingWording(
                    current,
                    success.optionalWording,
                    current.projectionFingerprint,
                    current.formatRevision,
                    current.categoryMask,
                    MemoryOptionalWordingDispositionTokens.Failed,
                    80));
            SummaryWordingResultPlan failed = MemoryOptionalAiPolicy.PlanSummaryResult(
                first, current, false, "ignored", 80);
            AssertTrue("optional.result.failure-keeps-deterministic",
                failed.identityMatched && !failed.applyOptionalWording
                    && failed.optionalWording.Length == 0);
            SummaryWordingResultPlan malformed = MemoryOptionalAiPolicy.PlanSummaryResult(
                first, current, true, "line one\nline two", 80);
            AssertEqual("optional.result.multiline-malformed",
                MemoryOptionalWordingDispositionTokens.Malformed,
                malformed.dispositionToken);
            current.summaryFactsRevision++;
            SummaryWordingResultPlan stale = MemoryOptionalAiPolicy.PlanSummaryResult(
                first, current, true, "stale prose", 80);
            AssertTrue("optional.result.revision-stale-keeps-fallback",
                !stale.identityMatched && !stale.applyOptionalWording);
            AssertEqual("optional.result.stale-does-not-invent-saved-disposition",
                MemoryOptionalWordingDispositionTokens.None, stale.dispositionToken);
            current.summaryFactsRevision--;
            current.suppressed = true;
            AssertTrue("optional.result.suppressed-still-targets-same-projection",
                MemoryOptionalAiPolicy.TargetsCurrentSummaryProjection(first, current));
            AssertTrue("optional.result.suppressed-stale",
                !MemoryOptionalAiPolicy.PlanSummaryResult(
                    first, current, true, "hidden prose", 80).identityMatched);
            AssertEqual("optional.cache.suppressed-exposes-no-prompt-wording", string.Empty,
                MemoryOptionalAiPolicy.SelectNaturalWritingWording(
                    current,
                    success.optionalWording,
                    current.projectionFingerprint,
                    current.formatRevision,
                    current.categoryMask,
                    MemoryOptionalWordingDispositionTokens.Success,
                    80));
            current.suppressed = false;

            var projectedSummary = new MemoryReducerSummary();
            var projectedBucket = new MemoryReducerBucket
            {
                bucketKey = "topic",
                factKind = "topic",
                aggregationToken = MemoryFactContractTokens.OrdinalSet
            };
            projectedBucket.contributions.Add(new MemoryReducerContribution
            {
                contributionId = "personal",
                category = MemoryContractTokens.CategoryPersonal,
                canonicalValue = "kept"
            });
            projectedBucket.contributions.Add(new MemoryReducerContribution
            {
                contributionId = "family",
                category = MemoryContractTokens.CategoryFamily,
                canonicalValue = "hidden"
            });
            projectedSummary.factBuckets.Add(projectedBucket);
            string projectedWording;
            AssertTrue("optional.projection.personal-builds",
                MemoryThreadReducer.TryBuildDeterministicCategoryProjection(
                    projectedSummary, MemoryCategoryBits.Personal, 240,
                    out projectedWording));
            AssertEqual("optional.projection.excludes-disabled-category",
                "topic=kept", projectedWording);
            AssertTrue("optional.projection.family-builds",
                MemoryThreadReducer.TryBuildDeterministicCategoryProjection(
                    projectedSummary, MemoryCategoryBits.Family, 240,
                    out projectedWording));
            AssertEqual("optional.projection.selects-frozen-family-mask",
                "topic=hidden", projectedWording);
            AssertTrue("optional.projection.no-matching-category-refuses",
                !MemoryThreadReducer.TryBuildDeterministicCategoryProjection(
                    projectedSummary, MemoryCategoryBits.Factions, 240,
                    out projectedWording));
            AssertEqual("optional.projection.source-remains-detached", 2,
                projectedSummary.factBuckets[0].contributions.Count);

            var unicodeSummary = new MemoryReducerSummary();
            var unicodeBucket = new MemoryReducerBucket
            {
                bucketKey = "u",
                factKind = "u",
                aggregationToken = MemoryFactContractTokens.OrdinalSet
            };
            unicodeBucket.contributions.Add(new MemoryReducerContribution
            {
                contributionId = "unicode",
                category = MemoryContractTokens.CategoryPersonal,
                canonicalValue = "\ud83d\ude00"
            });
            unicodeSummary.factBuckets.Add(unicodeBucket);
            AssertTrue("optional.projection.unicode-cap-builds",
                MemoryThreadReducer.TryBuildDeterministicCategoryProjection(
                    unicodeSummary, MemoryCategoryBits.Personal, 3,
                    out projectedWording));
            AssertTrue("optional.projection.cap-is-surrogate-safe",
                projectedWording.Length <= 3
                    && MemoryIdentityCodec.IsWellFormedUtf16(projectedWording));

            MemoryOptionalRequestBuildInput requestInput = new MemoryOptionalRequestBuildInput
            {
                logicalRequestSequence = 501,
                requestPurposeToken = MemoryDispatchTokens.SummaryWording,
                sessionId = 7,
                opportunityKey = first.opportunityKey,
                povRoleToken = "initiator",
                ownerPawnId = first.ownerPawnId,
                ownerEpochToken = first.ownerEpochToken,
                ownerCancellationGeneration = 3,
                globalCancellationGeneration = 4,
                optionalRequestInvalidationGeneration = 5
            };
            requestInput.variants.Add(new MemoryOptionalPromptVariantInput
            {
                templateIdentity = "summary_wording:v1",
                contextDetailIdentity = "optional:v1",
                systemPrompt = "Frozen system prompt.",
                userPrompt = "Frozen user prompt.",
                evidence = new List<MemoryEvidenceIdentity>
                {
                    new MemoryEvidenceIdentity
                    {
                        recordId = OrdinalSegmentCodec.Segment("record"),
                        sourceOccurrenceId = OrdinalSegmentCodec.Segment("source"),
                        rootIdOrEmpty = OrdinalSegmentCodec.Segment("root")
                    }
                }
            });
            MemoryLogicalRequestSnapshot built;
            AssertTrue("optional.request.complete-frozen-graph",
                MemoryOptionalAiPolicy.TryBuildLogicalRequest(requestInput, out built));
            AssertTrue("optional.request.common-policy-validates",
                MemoryDispatchPolicy.ValidateRequest(built));
            AssertEqual("optional.request.one-logical-prompt-variant", 1,
                built.variants.Count);
            AssertEqual("optional.request.provider-prompt-has-no-transport-metadata",
                "Frozen user prompt.", built.variants[0].userPrompt);
            AssertEqual("optional.request.summary-one-evidence", 1,
                built.reservedEvidence.Count);
            AssertEqual("optional.request.summary-zero-guards", 0,
                built.reservedGuards.Count);
            requestInput.optionalRequestInvalidationGeneration = long.MaxValue;
            AssertTrue("optional.request.saturated-generation-refuses-before-staging",
                !MemoryOptionalAiPolicy.TryBuildLogicalRequest(requestInput, out built));
            requestInput.optionalRequestInvalidationGeneration = 5;
            requestInput.variants[0].diagnostics = new List<MemoryDiagnosticIdentity> { null };
            AssertTrue("optional.request.null-nested-diagnostic-refuses",
                !MemoryOptionalAiPolicy.TryBuildLogicalRequest(requestInput, out built));
            requestInput.variants[0].diagnostics.Clear();
            requestInput.variants[0].evidence.Add(null);
            AssertTrue("optional.request.null-nested-evidence-refuses",
                !MemoryOptionalAiPolicy.TryBuildLogicalRequest(requestInput, out built));
            requestInput.variants[0].evidence.RemoveAt(
                requestInput.variants[0].evidence.Count - 1);

            MemoryInvokedGenerationCutoffTable cutoffs =
                new MemoryInvokedGenerationCutoffTable();
            string requestOne;
            string requestTwo;
            MemoryIdentityCodec.TryCreateLogicalRequestId(601, out requestOne);
            MemoryIdentityCodec.TryCreateLogicalRequestId(602, out requestTwo);
            AssertTrue("optional.cutoff.register-first-generation",
                cutoffs.TryRegister(7, "Pawn_Optional", Epoch(91), 3, 4,
                    requestOne, 11, 2));
            AssertTrue("optional.cutoff.same-request-different-sequence-refuses-preflight",
                !cutoffs.CanRegister(7, "Pawn_Optional", Epoch(91), 3, 4,
                    requestOne, 12, 2)
                    && cutoffs.UnsettledRequestCount == 1);
            AssertEqual("optional.cutoff.unsealed-does-not-bypass", false,
                cutoffs.AllowsInvocationWinner(7, "Pawn_Optional", Epoch(91), 3, 4,
                    requestOne, 11));
            AssertEqual("optional.cutoff.seal-first-generation", 1,
                cutoffs.SealGeneration(7, 4, 11));
            AssertTrue("optional.cutoff.first-cycle-invocation-wins",
                cutoffs.AllowsInvocationWinner(7, "Pawn_Optional", Epoch(91), 3, 4,
                    requestOne, 11));
            AssertTrue("optional.cutoff.register-second-generation",
                cutoffs.TryRegister(7, "Pawn_Optional", Epoch(91), 3, 5,
                    requestTwo, 12, 2));
            cutoffs.SealGeneration(7, 5, 12);
            AssertTrue("optional.cutoff.repeated-off-on-keeps-old-live-entry",
                cutoffs.AllowsInvocationWinner(7, "Pawn_Optional", Epoch(91), 3, 4,
                    requestOne, 11)
                    && cutoffs.AllowsInvocationWinner(7, "Pawn_Optional", Epoch(91), 3, 5,
                        requestTwo, 12));
            AssertTrue("optional.cutoff.brainwipe-epoch-never-aliases",
                !cutoffs.AllowsInvocationWinner(7, "Pawn_Optional", Epoch(92), 3, 4,
                    requestOne, 11));
            AssertTrue("optional.cutoff.settlement-prunes-one-exact-request",
                cutoffs.Settle(requestOne));
            AssertEqual("optional.cutoff.one-entry-remains", 1, cutoffs.EntryCount);
            cutoffs.Settle(requestTwo);
            AssertEqual("optional.cutoff.empty-prunes-all", 0, cutoffs.EntryCount);

            var saturatedCutoffs = new MemoryInvokedGenerationCutoffTable();
            AssertTrue("optional.cutoff.live-entry-registers-at-cap",
                saturatedCutoffs.TryRegister(7, "Pawn_Optional", Epoch(91), 3, 4,
                    requestOne, 11, 1));
            AssertTrue("optional.cutoff.same-generation-second-request-refuses-at-cap",
                !saturatedCutoffs.TryRegister(7, "Pawn_Optional", Epoch(91), 3, 4,
                    requestTwo, 12, 1)
                    && saturatedCutoffs.EntryCount == 1
                    && saturatedCutoffs.UnsettledRequestCount == 1);
            AssertTrue("optional.cutoff.cap-refuses-without-evicting-live-entry",
                !saturatedCutoffs.TryRegister(7, "Pawn_Optional", Epoch(91), 3, 5,
                    requestTwo, 12, 1)
                    && saturatedCutoffs.EntryCount == 1
                    && saturatedCutoffs.UnsettledRequestCount == 1);
            AssertTrue("optional.cutoff.saturated-generation-refuses",
                !saturatedCutoffs.TryRegister(7, "Pawn_Optional", Epoch(91), 3,
                    long.MaxValue, requestTwo, 12, 2));
        }

        private static SummaryWordingOpportunitySnapshot SummaryOpportunity(
            string owner,
            string epoch,
            string root,
            string summary,
            int priority,
            int salience,
            long requested)
        {
            return new SummaryWordingOpportunitySnapshot
            {
                ownerPawnId = owner,
                ownerEpochToken = epoch,
                ownerCancellationGeneration = 2,
                globalCancellationGeneration = 3,
                optionalRequestInvalidationGeneration = 4,
                rootId = OrdinalSegmentCodec.Segment(root),
                summaryRecordId = OrdinalSegmentCodec.Segment(summary),
                expectedRootStructuralRevision = 8,
                expectedSummaryFactsRevision = 9,
                expectedReducerRevision = 1,
                expectedFormatRevision = 1,
                expectedCategoryMask = 3,
                projectionFingerprint = new string('a', 64),
                requestedTick = requested,
                dueTick = requested,
                expiryTick = requested + 100,
                configuredPriority = priority,
                salience = salience
            };
        }

        private static void TestAcceptedPromptRetention()
        {
            DiaryAcceptedPromptUnit initiator = new DiaryAcceptedPromptUnit
            {
                eventTick = 10,
                eventId = "event-a",
                povRole = "initiator",
                systemPrompt = "s&<",
                userPrompt = "u>"
            };
            DiaryAcceptedPromptUnit recipient = new DiaryAcceptedPromptUnit
            {
                eventTick = 10,
                eventId = "event-a",
                povRole = "recipient",
                systemPrompt = "system",
                userPrompt = string.Empty
            };
            DiaryAcceptedPromptUnit neutral = new DiaryAcceptedPromptUnit
            {
                eventTick = 10,
                eventId = "event-a",
                povRole = "neutral",
                systemPrompt = "system",
                userPrompt = "user"
            };
            DiaryAcceptedPromptRetentionPlan countPlan =
                DiaryAcceptedPromptRetentionPolicy.Plan(
                    new[] { neutral, recipient, initiator }, 2, long.MaxValue);
            AssertTrue("accepted.retention.count.valid", countPlan.valid);
            AssertEqual("accepted.retention.count.one-cleared", 1,
                countPlan.clearOldestPrefix.Count);
            AssertTrue("accepted.retention.role-order",
                ReferenceEquals(initiator, countPlan.clearOldestPrefix[0]));

            long escapedCharge = DiaryAcceptedPromptRetentionPolicy.Charge(initiator);
            AssertEqual("accepted.retention.xml-escaped-charge",
                256L + 10L + 5L, escapedCharge);
            DiaryAcceptedPromptRetentionPlan bytePlan =
                DiaryAcceptedPromptRetentionPolicy.Plan(
                    new[] { initiator }, 1, escapedCharge - 1);
            AssertTrue("accepted.retention.byte.valid", bytePlan.valid);
            AssertEqual("accepted.retention.byte-clears-whole-unit", 1,
                bytePlan.clearOldestPrefix.Count);
            AssertEqual("accepted.retention.byte-empty-total", 0L,
                bytePlan.retainedEscapedBytes);
            AssertTrue("accepted.retention.legacy-half-counted",
                DiaryAcceptedPromptRetentionPolicy.Charge(recipient)
                    > DiaryAcceptedPromptRetentionPolicy.AcceptedPromptPairOverheadV1);
        }

        private static MemoryLogicalRequestSnapshot BuildDispatchRequest()
        {
            MemoryLogicalRequestSnapshot request = new MemoryLogicalRequestSnapshot
            {
                logicalRequestSequence = 17,
                requestPurposeToken = MemoryDispatchTokens.NormalDiary,
                sessionId = 9,
                eventIdOrOpportunityKey = "Event_Dispatch_17",
                povRoleToken = "initiator",
                ownerPawnId = "Pawn_Dispatch",
                ownerEpochToken = Epoch(17),
                ownerCancellationGeneration = 4,
                globalCancellationGeneration = 7,
                optionalRequestInvalidationGeneration = 0,
                requestStateToken = MemoryRequestStateMachineContracts.Activated
            };
            AssertTrue("dispatch.helper.request-id",
                MemoryIdentityCodec.TryCreateLogicalRequestId(
                    request.logicalRequestSequence, out request.logicalRequestId));

            MemoryEvidenceIdentity firstEvidence = new MemoryEvidenceIdentity
            {
                recordId = "record-a",
                sourceOccurrenceId = "source-a",
                rootIdOrEmpty = "root-a"
            };
            MemoryEvidenceIdentity secondEvidence = new MemoryEvidenceIdentity
            {
                recordId = "record-b",
                sourceOccurrenceId = "source-b",
                rootIdOrEmpty = "root-b"
            };
            MemoryGuardIdentity guard = new MemoryGuardIdentity
            {
                guardKind = "record",
                guardKey = "guard-a"
            };
            request.reservedEvidence.Add(firstEvidence);
            request.reservedEvidence.Add(secondEvidence);
            request.reservedGuards.Add(guard);

            request.variants.Add(BuildDispatchVariant(
                request, 0, "detail-full", "system-full", "user-full",
                new List<MemoryEvidenceIdentity> { secondEvidence, firstEvidence }, guard));
            request.variants.Add(BuildDispatchVariant(
                request, 1, "detail-compact", "system-compact", "user-compact",
                new List<MemoryEvidenceIdentity> { firstEvidence }, guard));

            AssertTrue("dispatch.helper.evidence-epoch",
                MemoryIdentityCodec.TryCreateEvidenceEpochToken(
                    request.requestPurposeToken,
                    request.eventIdOrOpportunityKey,
                    request.povRoleToken,
                    request.ownerPawnId,
                    request.ownerEpochToken,
                    request.reservedEvidence,
                    request.reservedGuards,
                    out request.evidenceEpochToken));
            AssertTrue("dispatch.helper.request-key",
                MemoryIdentityCodec.TryCreateLogicalRequestKey(
                    request.requestPurposeToken,
                    request.eventIdOrOpportunityKey,
                    request.povRoleToken,
                    request.ownerPawnId,
                    request.ownerEpochToken,
                    request.evidenceEpochToken,
                    out request.logicalRequestKey));
            return request;
        }

        private static MemoryFrozenPromptVariantSnapshot BuildDispatchVariant(
            MemoryLogicalRequestSnapshot request,
            int ordinal,
            string detail,
            string systemPrompt,
            string userPrompt,
            List<MemoryEvidenceIdentity> evidence,
            MemoryGuardIdentity guard)
        {
            MemoryFrozenPromptVariantSnapshot variant =
                new MemoryFrozenPromptVariantSnapshot
                {
                    variantOrdinal = ordinal,
                    templateIdentity = "template-v1",
                    contextDetailIdentity = detail,
                    systemPrompt = systemPrompt,
                    userPrompt = userPrompt
                };
            variant.receipt.evidence.AddRange(evidence);
            variant.receipt.guards.Add(guard);
            AssertTrue("dispatch.helper.evidence-fingerprint." + ordinal,
                MemoryIdentityCodec.TryCreateEvidenceSetFingerprint(
                    variant.receipt.evidence,
                    out variant.receipt.evidenceSetFingerprint));
            AssertTrue("dispatch.helper.receipt-fingerprint." + ordinal,
                MemoryIdentityCodec.TryCreateReceiptPlanFingerprint(
                    variant.receipt.evidence,
                    variant.receipt.guards,
                    out variant.receipt.receiptPlanFingerprint));
            string diagnosticFingerprint;
            AssertTrue("dispatch.helper.diagnostic-fingerprint." + ordinal,
                MemoryIdentityCodec.TryCreateDiagnosticProvenanceFingerprint(
                    variant.diagnostics, out diagnosticFingerprint));
            AssertTrue("dispatch.helper.variant-key." + ordinal,
                MemoryIdentityCodec.TryCreatePromptVariantKey(
                    request.logicalRequestId,
                    ordinal,
                    request.requestPurposeToken,
                    variant.templateIdentity,
                    variant.contextDetailIdentity,
                    variant.systemPrompt,
                    variant.userPrompt,
                    variant.receipt.receiptPlanFingerprint,
                    diagnosticFingerprint,
                    out variant.variantKey));
            return variant;
        }

        private static MemoryDispatchFenceSnapshot DispatchFence(
            MemoryLogicalRequestSnapshot request)
        {
            return new MemoryDispatchFenceSnapshot
            {
                sessionId = request.sessionId,
                ownerPawnId = request.ownerPawnId,
                ownerEpochToken = request.ownerEpochToken,
                ownerCancellationGeneration = request.ownerCancellationGeneration,
                globalCancellationGeneration = request.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    request.optionalRequestInvalidationGeneration
            };
        }

        private static void TestExactRoutePolicy()
        {
            MemoryThreadRouteRule counterpartRoute = Route("pawn", "counterpart_pawn");
            List<MemoryRouteCandidate> relationshipAndFaction = new List<MemoryRouteCandidate>
            {
                Candidate("counterpart_pawn", "pawn", "Pawn_B", "Alex"),
                Candidate("unlisted_faction", "faction", "Faction_1", "Alex")
            };
            MemoryRouteResolution resolved = MemoryThreadRoutingPolicy.Resolve(
                "Pawn_A", counterpartRoute, relationshipAndFaction);
            AssertTrue("route.declared.accepted", resolved.isThreaded);
            AssertEqual("route.declared.kind", "pawn", resolved.subjectKind);
            AssertEqual("route.declared.id", "Pawn_B", resolved.subjectId);
            AssertEqual("route.unlisted.ignored", "Alex", resolved.frozenLabel);

            MemoryThreadRouteRule birthRoute = Route("pawn", "context:child_id");
            resolved = MemoryThreadRoutingPolicy.Resolve("Pawn_A", birthRoute, new[]
            {
                Candidate("context:child_id", "pawn", "Pawn_Child", "Mira"),
                Candidate("counterpart_pawn", "pawn", "Pawn_Parent", "Brik")
            });
            AssertTrue("route.birth.child", resolved.isThreaded && resolved.subjectId == "Pawn_Child");

            resolved = MemoryThreadRoutingPolicy.Resolve("Pawn_A", counterpartRoute, new[]
            {
                Candidate("counterpart_pawn", "pawn", "Pawn_B", "Same"),
                Candidate("counterpart_pawn", "pawn", "Pawn_C", "Same")
            });
            AssertEqual("route.ambiguous.standalone",
                MemoryThreadRoutingPolicy.StandaloneAmbiguousIdentity, resolved.reasonToken);
            AssertTrue("route.ambiguous.not-threaded", !resolved.isThreaded);

            resolved = MemoryThreadRoutingPolicy.Resolve("Pawn_A", counterpartRoute, new[]
            {
                Candidate("counterpart_pawn", "pawn", "Pawn_A", "Owner")
            });
            AssertEqual("route.owner-self.standalone",
                MemoryThreadRoutingPolicy.StandaloneOwnerSelf, resolved.reasonToken);

            resolved = MemoryThreadRoutingPolicy.Resolve("Pawn_A", null, relationshipAndFaction);
            AssertEqual("route.absent.standalone",
                MemoryThreadRoutingPolicy.StandaloneNoRoute, resolved.reasonToken);

            MemoryThreadRouteRule orderedFallbacks = Route(
                "pawn", "context:primary", "context:fallback");
            resolved = MemoryThreadRoutingPolicy.Resolve("Pawn_A", orderedFallbacks, new[]
            {
                Candidate("context:fallback", "pawn", "Pawn_B", "Fallback label"),
                Candidate("context:primary", "pawn", "Pawn_B", "Primary label")
            });
            AssertEqual("route.equivalent.declaration-order",
                "Primary label", resolved.frozenLabel);
            resolved = MemoryThreadRoutingPolicy.Resolve("Pawn_A", orderedFallbacks, new[]
            {
                Candidate("context:primary", "pawn", "Pawn_B", "Primary label"),
                Candidate("context:fallback", "pawn", "Pawn_B", "Fallback label")
            });
            AssertEqual("route.equivalent.input-permutation",
                "Primary label", resolved.frozenLabel);

            MemoryThreadRouteRule streamRoute = Route("stream", "constant:body_history");
            resolved = MemoryThreadRoutingPolicy.Resolve("Pawn_A", streamRoute, new[]
            {
                Candidate("constant:body_history", "stream", "body_history", "Body")
            });
            AssertTrue("route.stream.allowlisted", resolved.isThreaded);
            resolved = MemoryThreadRoutingPolicy.Resolve("Pawn_A", streamRoute, new[]
            {
                Candidate("constant:body_history", "stream", "invented_stream", "Body")
            });
            AssertEqual("route.stream.unknown.standalone",
                MemoryThreadRoutingPolicy.StandaloneMissingIdentity, resolved.reasonToken);
            resolved = MemoryThreadRoutingPolicy.Resolve("Pawn_A", streamRoute, new[]
            {
                Candidate("constant:body_history", "stream", "belief", "Belief")
            });
            AssertEqual("route.stream.wrong-allowlisted-constant.standalone",
                MemoryThreadRoutingPolicy.StandaloneMissingIdentity, resolved.reasonToken);
            resolved = MemoryThreadRoutingPolicy.Resolve(
                "Pawn_A",
                Route("stream", "constant:body_history", "constant:belief"),
                new[]
                {
                    Candidate("constant:body_history", "stream", "body_history", "Body")
                });
            AssertEqual("route.stream.mixed-constants.standalone",
                MemoryThreadRoutingPolicy.StandaloneMissingIdentity, resolved.reasonToken);

            string canonicalFaction = Faction("Faction_1", 1);
            resolved = MemoryThreadRoutingPolicy.Resolve(
                "Pawn_A",
                Route("faction", "context:faction_id"),
                new[]
                {
                    Candidate("context:faction_id", "faction", canonicalFaction, "Union")
                });
            AssertTrue("route.faction.canonical", resolved.isThreaded);
            resolved = MemoryThreadRoutingPolicy.Resolve(
                "Pawn_A",
                Route("faction", "context:faction_id"),
                new[]
                {
                    Candidate("context:faction_id", "faction", "Faction_1", "Union")
                });
            AssertEqual("route.faction.noncanonical.standalone",
                MemoryThreadRoutingPolicy.StandaloneMissingIdentity, resolved.reasonToken);

            // Relationship phase/label are not root fields: every phase about the same exact pawn
            // creates the byte-identical root, while equal labels on distinct IDs do not collide.
            string spouse;
            string enemy;
            string sameLabelOtherId;
            MemoryIdentityCodec.TryCreateRootId(Root("Pawn_A", Epoch(1), "pawn", "Pawn_B"), out spouse);
            MemoryIdentityCodec.TryCreateRootId(Root("Pawn_A", Epoch(1), "pawn", "Pawn_B"), out enemy);
            MemoryIdentityCodec.TryCreateRootId(Root("Pawn_A", Epoch(1), "pawn", "Pawn_C"), out sameLabelOtherId);
            AssertEqual("route.phase.same-root", spouse, enemy);
            AssertTrue("route.equal-label.distinct-id", spouse != sameLabelOtherId);
        }

        private static void TestFactGrammar()
        {
            MemoryFactDescriptor count = Fact("occurrence", "count_occurrences", "empty");
            MemoryFactDescriptor ordinal = Fact("role", "ordinal_set", "ordinal");
            MemoryFactDescriptor range = Fact("opinion", "int64_range", "int64");
            MemoryFactDescriptor state = Fact("relation", "latest_state", "state");
            state.allowedStates.AddRange(new[] { "hostile", "neutral", "ally" });

            AssertTrue("fact.count.empty", MemoryThreadRoutingPolicy.IsValidCanonicalValue(count, ""));
            AssertTrue("fact.count.text.reject", !MemoryThreadRoutingPolicy.IsValidCanonicalValue(count, "1"));
            AssertTrue("fact.ordinal", MemoryThreadRoutingPolicy.IsValidCanonicalValue(ordinal, "MoralGuide"));
            AssertTrue("fact.ordinal.blank.reject", !MemoryThreadRoutingPolicy.IsValidCanonicalValue(ordinal, " "));
            foreach (string value in new[] { "0", "1", "-1", long.MaxValue.ToString(), long.MinValue.ToString() })
                AssertTrue("fact.int64." + value, MemoryThreadRoutingPolicy.IsValidCanonicalValue(range, value));
            foreach (string value in new[] { "+1", "01", "-0", "--1", "9223372036854775808" })
                AssertTrue("fact.int64.reject." + value,
                    !MemoryThreadRoutingPolicy.IsValidCanonicalValue(range, value));
            AssertTrue("fact.state.allowed", MemoryThreadRoutingPolicy.IsValidCanonicalValue(state, "ally"));
            AssertTrue("fact.state.unknown.reject", !MemoryThreadRoutingPolicy.IsValidCanonicalValue(state, "friend"));
            state.canonicalValueKind = "ordinal";
            AssertTrue("fact.mixed-grammar.reject", !MemoryThreadRoutingPolicy.IsValidCanonicalValue(state, "ally"));
        }

        private static void TestSettingsAndCapacityContracts()
        {
            AssertEqual("activation.m0", MemorySystemActivationGate.LegacyShadow,
                MemorySystemActivationGate.BuildState);
            MemorySettingsPolicyFieldsV1 release = new MemorySettingsPolicyFieldsV1();
            AssertTrue("settings.release.extra-off", !release.allowExtraMemoryAiRequests);
            AssertTrue("settings.release.quiet-off", !release.occasionalMemoryReflections);
            AssertEqual("settings.release.mask", 15, release.memoryCategoryMask);
            AssertEqual("settings.release.ttl", 15, release.minorMemoryLifetimeDays);
            AssertEqual("settings.release.regular", 60, release.regularMemoryLifetimeDays);
            AssertEqual("settings.release.target", 12, release.memoryThreadTarget);
            MemorySettingsPolicyFieldsV1 benchmark =
                MemorySettingsPolicyFieldsV1.CreateBenchmarkProfile(64);
            AssertTrue("settings.benchmark.extra-on", benchmark.allowExtraMemoryAiRequests);
            AssertTrue("settings.benchmark.quiet-on", benchmark.occasionalMemoryReflections);
            AssertEqual("settings.benchmark.target", 64, benchmark.memoryThreadTarget);
            string releaseEncoding = MemorySettingsPolicyCodec.Encode(release);
            AssertTrue("settings.encoding.domain",
                releaseEncoding.StartsWith("32:memory-settings-policy-fields-v1", StringComparison.Ordinal));
            AssertTrue("settings.encoding.benchmark-distinct",
                releaseEncoding != MemorySettingsPolicyCodec.Encode(benchmark));

            List<MemoryCapacityContractRow> production = MemoryCapacityContracts.ProvisionalProduction();
            List<MemoryCapacityContractRow> defensive = MemoryCapacityContracts.DefensiveCeilings();
            AssertEqual("capacity.production.count", 64, production.Count);
            AssertEqual("capacity.defensive.count", 64, defensive.Count);
            for (int index = 0; index < production.Count; index++)
            {
                AssertEqual("capacity.order." + index, production[index].name, defensive[index].name);
                ulong[] low = UnsignedTuple(production[index].valueEncoding);
                ulong[] high = UnsignedTuple(defensive[index].valueEncoding);
                AssertEqual("capacity.arity." + production[index].name, low.Length, high.Length);
                for (int member = 0; member < low.Length; member++)
                    AssertTrue("capacity.ceiling." + production[index].name + "." + member,
                        low[member] <= high[member]);
            }
            AssertEqual("capacity.identity.raw.ceiling",
                (ulong)MemoryIdentityCodec.MaximumRawIdentityCharacters,
                UnsignedTuple(defensive.First(row => row.name == "rawIdentitySegmentUnits").valueEncoding)[0]);
            ulong[] keyCeilings = UnsignedTuple(defensive.First(row => row.name == "compositeKeyUnits").valueEncoding);
            AssertEqual("capacity.identity.embedded.ceiling",
                (ulong)MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters, keyCeilings[0]);
            AssertEqual("capacity.identity.key.ceiling",
                (ulong)MemoryIdentityCodec.MaximumCompleteKeyCharacters, keyCeilings[1]);
        }

        private static void TestShippedXmlContractsAndReachability()
        {
            string root = RepoRoot();
            XDocument document = XDocument.Load(Path.Combine(root, "1.6", "Defs", "DiaryImportantEventDefs.xml"));
            List<XElement> defs = document.Root.Elements("PawnDiary.DiaryImportantEventDef").ToList();
            AssertEqual("xml.capture.count", 34, defs.Count);
            int standalone = 0;
            foreach (XElement def in defs)
            {
                ImportantEventRule rule = new ImportantEventRule
                {
                    defName = Text(def, "defName"),
                    captureSourceToken = Text(def, "captureSourceToken"),
                    memoryKind = Text(def, "memoryKind"),
                    memoryCategory = Text(def, "memoryCategory"),
                    baseImportance = Text(def, "baseImportance"),
                    consolidationEligible = Bool(def, "consolidationEligible"),
                    authoritativePageOwned = Bool(def, "authoritativePageOwned")
                };
                XElement facts = def.Element("memoryFacts");
                foreach (XElement fact in facts.Elements("li"))
                {
                    MemoryFactDescriptor descriptor = Fact(
                        Text(fact, "factKind"), Text(fact, "aggregationToken"),
                        Text(fact, "canonicalValueKind"));
                    descriptor.contextKey = Text(fact, "contextKey");
                    XElement states = fact.Element("allowedStates");
                    if (states != null) descriptor.allowedStates.AddRange(states.Elements("li").Select(row => row.Value));
                    rule.memoryFacts.Add(descriptor);
                }
                XElement route = def.Element("threadRoute");
                if (route == null) standalone++;
                else
                {
                    rule.threadRoute = new MemoryThreadRouteRule
                    {
                        subjectKind = Text(route, "subjectKind"),
                        chapterPhasePolicy = Text(route, "chapterPhasePolicy"),
                        chapterDirective = string.IsNullOrEmpty(Text(route, "chapterDirective"))
                            ? MemoryChapterDirectiveTokens.ContinueCurrent
                            : Text(route, "chapterDirective"),
                        chapterClosureReasonToken = Text(route, "chapterClosureReasonToken"),
                        fallbackLabelSource = Text(route, "fallbackLabelSource")
                    };
                    rule.threadRoute.equivalentExtractors.AddRange(
                        route.Element("equivalentExtractors").Elements("li")
                            .Select(row => new MemoryRouteExtractor { extractorToken = row.Value }));
                }
                rule.promptConsumerIds.AddRange(def.Element("promptConsumerIds").Elements("li").Select(row => row.Value));
                AssertEqual("xml.capture.valid." + rule.defName, string.Empty,
                    MemoryThreadRoutingPolicy.ValidateRuleContract(rule));
            }
            AssertEqual("xml.capture.standalone", 1, standalone);
            AssertEqual("xml.capture.four-categories", 4,
                defs.Select(def => Text(def, "memoryCategory"))
                    .Distinct(StringComparer.Ordinal).Count());
            List<string> shippedStreamTokens = defs
                .Select(def => def.Element("threadRoute"))
                .Where(route => route != null && Text(route, "subjectKind") == "stream")
                .SelectMany(route => route.Element("equivalentExtractors").Elements("li"))
                .Select(row => row.Value.Substring("constant:".Length))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
            AssertEqual("xml.stream.allowlist.parity",
                string.Join("/", MemoryContractTokens.StreamSubjectTokens()
                    .OrderBy(value => value, StringComparer.Ordinal)),
                string.Join("/", shippedStreamTokens));

            List<MemoryRecallConsumerContract> consumers = MemoryRecallConsumerRegistry.All();
            AssertEqual("consumer.count", 7, consumers.Count);
            AssertEqual("consumer.unique", consumers.Count,
                consumers.Select(row => row.consumerId).Distinct(StringComparer.Ordinal).Count());
            foreach (MemoryRecallConsumerContract consumer in consumers)
            {
                AssertTrue("consumer.common-exclusion." + consumer.consumerId,
                    consumer.appliesCommonExclusionContract);
                AssertEqual("consumer.compact.zero." + consumer.consumerId, 0, consumer.compactMaximumLines);
                AssertEqual("consumer.off.zero." + consumer.consumerId, 0, consumer.offMaximumLines);
                AssertTrue("consumer.character-cap." + consumer.consumerId,
                    !string.IsNullOrEmpty(consumer.characterCapDimensionToken));
                AssertTrue("consumer.owner-check." + consumer.consumerId,
                    consumer.requiresOwnerMatch);
                AssertTrue("consumer.epoch-check." + consumer.consumerId,
                    consumer.requiresEpochMatch);
                AssertTrue("consumer.category-check." + consumer.consumerId,
                    consumer.requiresCategoryEnabled);
                AssertTrue("consumer.suppression-check." + consumer.consumerId,
                    consumer.honorsSuppression);
                if (consumer.consumerId == MemoryRecallConsumerRegistry.SummaryWording)
                {
                    AssertEqual("consumer.summary.formats", 0, consumer.eligibleWritingFormats.Count);
                    AssertTrue("consumer.summary.current-event-not-applicable",
                        !consumer.excludesCurrentEvent);
                }
                else
                {
                    AssertEqual("consumer.writing.formats." + consumer.consumerId,
                        "Full/Balanced", string.Join("/", consumer.eligibleWritingFormats));
                    AssertTrue("consumer.current-event." + consumer.consumerId,
                        consumer.excludesCurrentEvent);
                }
            }

            XDocument tuning = XDocument.Load(Path.Combine(root, "1.6", "Defs", "DiaryKnowledgeTuningDef.xml"));
            XElement tuningDef = tuning.Root.Element("PawnDiary.DiaryKnowledgeTuningDef");
            List<XElement> vector = tuningDef.Element("memoryCapacityVector").Elements("li").ToList();
            List<MemoryCapacityContractRow> production = MemoryCapacityContracts.ProvisionalProduction();
            AssertEqual("xml.capacity.count", production.Count, vector.Count);
            for (int index = 0; index < production.Count; index++)
            {
                AssertEqual("xml.capacity.name." + index, production[index].name, Text(vector[index], "name"));
                AssertEqual("xml.capacity.value." + index, production[index].valueEncoding,
                    Text(vector[index], "valueEncoding"));
            }
            AssertEqual("xml.ttl.min", "1", Text(tuningDef, "minorMemoryLifetimeMinimumDays"));
            AssertEqual("xml.ttl.min.default", "15", Text(tuningDef, "minorMemoryLifetimeDefaultDays"));
            AssertEqual("xml.ttl.regular.default", "60", Text(tuningDef, "regularMemoryLifetimeDefaultDays"));
            AssertEqual("xml.thread.range", "4/12/64",
                Text(tuningDef, "memoryThreadTargetMinimum") + "/"
                + Text(tuningDef, "memoryThreadTargetDefault") + "/"
                + Text(tuningDef, "memoryThreadTargetMaximum"));
            XDocument russianTuning = XDocument.Load(Path.Combine(
                root,
                "Languages",
                "Russian (Русский)",
                "DefInjected",
                "PawnDiary.DiaryKnowledgeTuningDef",
                "DiaryKnowledgeTuningDef.xml"));
            string[] optionalPromptFields =
            {
                "memoryReflectionSystemPrompt",
                "memoryReflectionLabel",
                "memoryReflectionInstruction",
                "summaryWordingSystemPrompt",
                "summaryWordingInstruction"
            };
            for (int index = 0; index < optionalPromptFields.Length; index++)
            {
                string key = "Diary_Knowledge." + optionalPromptFields[index];
                XElement translated = russianTuning.Root.Element(key);
                AssertTrue("xml.optional-ai.russian-def-injected." + optionalPromptFields[index],
                    translated != null && !string.IsNullOrWhiteSpace(translated.Value));
            }
        }

        private static void TestM0CatalogShape()
        {
            string root = RepoRoot();
            string catalogDir = Path.Combine(root, "benchmarks", "MemoryThreadBenchmarks", "Catalog");
            using (JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(catalogDir, "memory-m0-fixture-catalog-v1.json"))))
            {
                JsonElement value = fixture.RootElement;
                AssertEqual("catalog.fixture.schema", "memory-m0-fixture-catalog-v1",
                    value.GetProperty("schema").GetString());
                AssertEqual("catalog.fixture.activation", MemorySystemActivationGate.BuildState,
                    value.GetProperty("activationBuildState").GetString());
                AssertEqual("catalog.fixture.N", "4/12/64",
                    string.Join("/", value.GetProperty("threadTargets").EnumerateArray().Select(row => row.GetInt32())));
                AssertEqual("catalog.fixture.loaded-pending", 5,
                    value.GetProperty("loadedPendingFixtures").GetArrayLength());
                AssertEqual("catalog.fixture.request-invariants", 6,
                    value.GetProperty("requestStateMachine").GetProperty("invariants").GetArrayLength());
                JsonElement stateMachine = value.GetProperty("requestStateMachine");
                AssertEqual("catalog.fixture.request-schema",
                    MemoryRequestStateMachineContracts.SchemaToken,
                    stateMachine.GetProperty("schema").GetString());
                AssertEqual("catalog.fixture.request-states",
                    string.Join("/", MemoryRequestStateMachineContracts.States()),
                    string.Join("/", stateMachine.GetProperty("states").EnumerateArray()
                        .Select(row => row.GetString())));
                AssertEqual("catalog.fixture.request-transitions",
                    string.Join("/", MemoryRequestStateMachineContracts.Transitions()),
                    string.Join("/", stateMachine.GetProperty("transitions").EnumerateArray()
                        .Select(row => row.GetString())));
                AssertEqual("catalog.fixture.attempt-states",
                    string.Join("/", MemoryRequestStateMachineContracts.AttemptStates()),
                    string.Join("/", stateMachine.GetProperty("attemptStates").EnumerateArray()
                        .Select(row => row.GetString())));
                AssertEqual("catalog.fixture.request-identities",
                    "logicalRequestId/logicalRequestKey/evidenceEpochToken/variantOrdinal/variantKey/attemptOrdinal/invocationSequence/evidenceSetFingerprint/receiptPlanFingerprint/diagnosticProvenanceFingerprint/permitFingerprint",
                    string.Join("/", stateMachine.GetProperty("identities").EnumerateArray()
                        .Select(row => row.GetString())));
                JsonElement settings = value.GetProperty("settings");
                AssertEqual("catalog.fixture.settings-fields",
                    "saveNewMemories/useMemoriesInWriting/usePawnBackground/allowExtraMemoryAiRequests/occasionalMemoryReflections/memoryCategoryMask/captureInvalidationGenerationPersonal/captureInvalidationGenerationRelationships/captureInvalidationGenerationFamily/captureInvalidationGenerationFactions/optionalRequestInvalidationGeneration/minorMemoryLifetimeDays/regularMemoryLifetimeDays/memoryThreadTarget/memoryReuseDays/memoryRevisitEntryCount",
                    string.Join("/", settings.GetProperty("fields").EnumerateArray()
                        .Select(row => row.GetString())));
                List<string> encodedDefaults = ReadSegments(
                    MemorySettingsPolicyCodec.Encode(new MemorySettingsPolicyFieldsV1()));
                AssertEqual("catalog.fixture.settings-schema", encodedDefaults[0],
                    settings.GetProperty("schema").GetString());
                AssertEqual("catalog.fixture.settings-defaults",
                    string.Join("/", encodedDefaults.Skip(1)),
                    string.Join("/", settings.GetProperty("releaseDefaults").EnumerateArray()
                        .Select(row => row.GetString())));
            }
            using (JsonDocument payload = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(catalogDir, "memory-payload-atom-catalog-v1.json"))))
            {
                List<JsonElement> types = payload.RootElement.GetProperty("types").EnumerateArray().ToList();
                AssertEqual("catalog.payload.types", 32, types.Count);
                AssertEqual("catalog.payload.unique-types", types.Count,
                    types.Select(row => row.GetProperty("name").GetString())
                        .Distinct(StringComparer.Ordinal).Count());
                foreach (JsonElement type in types)
                {
                    List<string> fields = type.GetProperty("fields").EnumerateArray()
                        .Select(row => row.GetString()).ToList();
                    AssertTrue("catalog.payload.nonempty." + type.GetProperty("name").GetString(),
                        fields.Count > 0);
                    AssertEqual("catalog.payload.unique-fields." + type.GetProperty("name").GetString(),
                        fields.Count, fields.Distinct(StringComparer.Ordinal).Count());
                }
                List<string> expectedPaths = types.SelectMany(type =>
                    type.GetProperty("fields").EnumerateArray().Select(field =>
                        type.GetProperty("name").GetString() + "." + field.GetString())).ToList();
                List<JsonElement> atoms = payload.RootElement.GetProperty("atomRows")
                    .EnumerateArray().ToList();
                AssertEqual("catalog.payload.declared-field-count", 400, expectedPaths.Count);
                AssertEqual("catalog.payload.atom-count", 400, atoms.Count);
                for (int index = 0; index < atoms.Count; index++)
                {
                    JsonElement atom = atoms[index];
                    AssertEqual("catalog.payload.atom-ordinal." + index, index,
                        atom.GetProperty("pathOrdinal").GetInt32());
                    AssertEqual("catalog.payload.atom-path." + index, expectedPaths[index],
                        atom.GetProperty("canonicalFieldPath").GetString());
                    AssertTrue("catalog.payload.atom-scope." + index,
                        atom.GetProperty("scopeMask").GetArrayLength() > 0);
                    AssertTrue("catalog.payload.atom-kind." + index,
                        new[] { "bool", "int32", "int64", "string", "row", "nullable_row", "list" }
                            .Contains(atom.GetProperty("atomKindToken").GetString(),
                                StringComparer.Ordinal));
                    AssertTrue("catalog.payload.atom-candidate." + index,
                        atom.TryGetProperty("candidateValueEncoding", out JsonElement ignored));
                }
                Dictionary<string, string> atomKinds = atoms.ToDictionary(
                    atom => atom.GetProperty("canonicalFieldPath").GetString(),
                    atom => atom.GetProperty("atomKindToken").GetString(),
                    StringComparer.Ordinal);
                AssertEqual("catalog.payload.kind.knowledge-schema", "int32",
                    atomKinds["PawnKnowledgeState.schemaVersion"]);
                AssertEqual("catalog.payload.kind.request-schema", "int32",
                    atomKinds["SavedActiveLogicalRequestV1.schemaVersion"]);
                AssertEqual("catalog.payload.kind.save-new-memories", "bool",
                    atomKinds["SavedMemoryAppliedPolicyStateV1.saveNewMemories"]);
                AssertEqual("catalog.payload.kind.request-session", "int64",
                    atomKinds["SavedActiveLogicalRequestV1.sessionId"]);
                AssertEqual("catalog.payload.kind.variant-ordinal", "int32",
                    atomKinds["SavedFrozenPromptVariantV1.variantOrdinal"]);
                AssertEqual("catalog.payload.kind.reflection-quiet-day", "int32",
                    atomKinds["PawnReflectionStateMemoryFields.lastQuietMemoryEvaluatedAbsoluteDay"]);
                foreach (string path in new[]
                {
                    "SavedMemoryAttemptAuditRow.attemptOrdinal",
                    "DiaryGameComponentMemory.memoryComponentSchemaVersion",
                    "DiaryGameComponentMemory.memoryCoordinatorSchemaVersion",
                    "DiaryGameComponentMemory.memoryDispatchSchemaVersion"
                })
                {
                    AssertEqual("catalog.payload.kind.int32." + path, "int32", atomKinds[path]);
                }
                foreach (string path in new[]
                {
                    "SavedMemoryBlock.primarySubject",
                    "SavedImportedMemoryRow.primarySubject",
                    "SavedFrozenPromptVariantV1.receiptPlan",
                    "SavedLegacyUnresolvedOwnerArchiveInputV1.legacyRecord"
                })
                {
                    AssertEqual("catalog.payload.kind.nullable-row." + path,
                        "nullable_row", atomKinds[path]);
                }
                Dictionary<string, bool> freeText = atoms.ToDictionary(
                    atom => atom.GetProperty("canonicalFieldPath").GetString(),
                    atom => atom.GetProperty("freeTextModeEligible").GetBoolean(),
                    StringComparer.Ordinal);
                foreach (string path in new[]
                {
                    "SavedMemorySummaryPayload.lastSettledWordingFingerprint",
                    "SavedMemorySummaryPayload.lastWordingDispositionToken",
                    "SavedFrozenPromptVariantV1.contextDetailIdentity"
                })
                {
                    AssertTrue("catalog.payload.not-free-text." + path, !freeText[path]);
                }
            }
        }

        private static void TestSavedScalarSchemaRegistry()
        {
            // The code-owned registry must stay in exact byte-parity with the frozen M0 payload
            // catalog: same row names, same field names, same atom kinds, no extra/missing paths.
            string payloadPath = System.IO.Path.Combine(
                RepoRoot(),
                "benchmarks", "MemoryThreadBenchmarks", "Catalog",
                "memory-payload-atom-catalog-v1.json");
            var payload = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(payloadPath));
            Dictionary<string, string> catalogKinds =
                new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> catalogRows = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in payload.RootElement.GetProperty("types").EnumerateArray())
            {
                catalogRows.Add(type.GetProperty("name").GetString());
            }

            foreach (var atom in payload.RootElement.GetProperty("atomRows").EnumerateArray())
            {
                catalogKinds[atom.GetProperty("canonicalFieldPath").GetString()] =
                    atom.GetProperty("atomKindToken").GetString();
            }

            IReadOnlyList<MemorySavedRowFields> rows = MemorySavedScalarSchema.Rows();
            AssertEqual("schema.row-count", 32, rows.Count);

            // ORDERED parity against the frozen catalog: type order, field order inside every
            // type, and atom-kind sequences must match index-for-index — sets would silently
            // pass a reordered schema (§T6.0 "exactly once" is an ordered contract).
            var orderedTypes = new List<(string name, List<string> fields)>();
            foreach (var type in payload.RootElement.GetProperty("types").EnumerateArray())
            {
                var fieldList = new List<string>();
                foreach (var field in type.GetProperty("fields").EnumerateArray())
                {
                    fieldList.Add(field.GetString());
                }
                orderedTypes.Add((type.GetProperty("name").GetString(), fieldList));
            }
            AssertEqual("schema.type-order.count", orderedTypes.Count, rows.Count);
            for (int i = 0; i < orderedTypes.Count && i < rows.Count; i++)
            {
                AssertEqual("schema.type-order." + i, orderedTypes[i].name, rows[i].rowName);
                AssertEqual("schema.field-order.count." + orderedTypes[i].name,
                    orderedTypes[i].fields.Count, rows[i].atoms.Length);
                for (int j = 0; j < orderedTypes[i].fields.Count
                        && j < rows[i].atoms.Length; j++)
                {
                    AssertEqual("schema.field-order." + orderedTypes[i].name + "." + j,
                        orderedTypes[i].fields[j], rows[i].atoms[j].fieldNameToken);
                    AssertEqual(
                        "schema.kind-order." + orderedTypes[i].name + "."
                        + rows[i].atoms[j].fieldNameToken,
                        catalogKinds[orderedTypes[i].name + "." + orderedTypes[i].fields[j]],
                        KindToken(rows[i].atoms[j].atomKind));
                }
            }

            // Flattened atom-path order must equal the frozen atomRows ordinal sequence too.
            int registryAtoms = 0;
            foreach (MemorySavedRowFields row in rows)
            {
                registryAtoms += row.atoms.Length;
            }
            AssertEqual("schema.atom-count", 400, registryAtoms);
            AssertEqual("schema.catalog-atom-count", 400, catalogKinds.Count);

            // §T6.0's exhaustive Boolean set must equal the registry's bool atoms exactly.
            string[] expectedBooleans =
            {
                "PawnKnowledgeState.archiveOnly",
                "PawnKnowledgeState.epochFenceOnly",
                "SavedMemoryChapter.closed",
                "SavedMemoryBlock.ageUnknown",
                "SavedMemoryBlock.playerEdited",
                "SavedMemoryBlock.suppressed",
                "SavedMemoryBlock.requiredLifecycleLandmark",
                "SavedMemoryCanonicalFact.majorTurningPoint",
                "SavedMemoryCanonicalFact.reversal",
                "SavedMemoryFactContribution.ageUnknown",
                "SavedMemoryFactContribution.majorTurningPoint",
                "SavedMemoryFactContribution.reversal",
                "SavedImportedMemoryRow.ageUnknown",
                "SavedImportedSummaryContributionEvidenceV1.ageUnknown",
                "SavedImportedSummaryContributionEvidenceV1.majorTurningPoint",
                "SavedImportedSummaryContributionEvidenceV1.reversal",
                "SavedMemoryAppliedPolicyStateV1.saveNewMemories",
                "SavedMemoryAppliedPolicyStateV1.useMemoriesInWriting",
                "SavedMemoryAppliedPolicyStateV1.usePawnBackground",
                "SavedMemoryAppliedPolicyStateV1.allowExtraMemoryAiRequests",
                "SavedMemoryAppliedPolicyStateV1.occasionalMemoryReflections",
                "DiaryGameComponentMemory.unresolvedArchiveReattributionDisabled",
                "SavedMemoryAttemptAuditRow.potentialExposure",
                "SavedGlobalFactionSnapshot.defeated",
                "SavedGlobalFactionSnapshot.removed",
                "SavedActiveLogicalAttemptV1.potentialExposureApplied",
                "SavedActiveLogicalAttemptV1.narrativeUseApplied",
                "SavedActiveLogicalAttemptV1.resultApplied"
            };
            HashSet<string> boolPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (MemorySavedRowFields row in rows)
            {
                foreach (MemorySavedFieldAtom atom in row.atoms)
                {
                    if (atom.atomKind == MemorySavedAtomKind.Bool)
                    {
                        boolPaths.Add(row.rowName + "." + atom.fieldNameToken);
                    }
                }
            }

            AssertEqual("schema.bool-set.count", expectedBooleans.Length, boolPaths.Count);
            foreach (string expected in expectedBooleans)
            {
                AssertTrue("schema.bool-set.contains." + expected, boolPaths.Contains(expected));
            }

            // Spot-check the declared logical widths from §T6.0.
            AssertEqual("schema.width.bool", 1,
                MemorySavedScalarSchema.LogicalWidthBytes(MemorySavedAtomKind.Bool));
            AssertEqual("schema.width.int32", 4,
                MemorySavedScalarSchema.LogicalWidthBytes(MemorySavedAtomKind.Int32));
            AssertEqual("schema.width.int64", 8,
                MemorySavedScalarSchema.LogicalWidthBytes(MemorySavedAtomKind.Int64));
            AssertEqual("schema.width.nullable-row-presence", 1,
                MemorySavedScalarSchema.LogicalWidthBytes(MemorySavedAtomKind.NullableRow));
            AssertEqual("schema.width.string", 0,
                MemorySavedScalarSchema.LogicalWidthBytes(MemorySavedAtomKind.String));
        }

        private static void TestOwnerEnvelopeSchemaPolicy()
        {
            // New objects initialize directly to the current writable shape (§T6.1).
            PawnKnowledgeStateSchemaPolicy.VersionClass fresh =
                PawnKnowledgeStateSchemaPolicy.Classify(
                    PawnKnowledgeStateSchemaPolicy.CurrentVersion);
            AssertEqual("envelope.fresh.current",
                PawnKnowledgeStateSchemaPolicy.VersionClass.Current, fresh);

            // Missing pre-feature data reads as shipped legacy version 1 (the Scribe default).
            AssertEqual("envelope.missing.legacy",
                PawnKnowledgeStateSchemaPolicy.VersionClass.LegacyPendingMigration,
                PawnKnowledgeStateSchemaPolicy.Classify(1));
            AssertEqual("envelope.v2.legacy",
                PawnKnowledgeStateSchemaPolicy.VersionClass.LegacyPendingMigration,
                PawnKnowledgeStateSchemaPolicy.Classify(2));

            // Explicit zero stays raw malformed input until component migration resolves it.
            AssertEqual("envelope.zero.raw",
                PawnKnowledgeStateSchemaPolicy.VersionClass.RawLegacy,
                PawnKnowledgeStateSchemaPolicy.Classify(0));

            // A greater-than-current version is the whole-save downgrade failure boundary.
            AssertEqual("envelope.future.newer",
                PawnKnowledgeStateSchemaPolicy.VersionClass.NewerThanCurrent,
                PawnKnowledgeStateSchemaPolicy.Classify(4));
            AssertEqual("envelope.future.max",
                PawnKnowledgeStateSchemaPolicy.VersionClass.NewerThanCurrent,
                PawnKnowledgeStateSchemaPolicy.Classify(int.MaxValue));

            // Only the current version may be written or stamped by a component commit.
            for (int version = 0; version <= 4; version++)
            {
                bool canWrite =
                    PawnKnowledgeStateSchemaPolicy.Classify(version)
                    == PawnKnowledgeStateSchemaPolicy.VersionClass.Current;
                AssertEqual("envelope.can-write." + version,
                    version == PawnKnowledgeStateSchemaPolicy.CurrentVersion ? 1 : 0,
                    canWrite ? 1 : 0);
            }
        }

        private static void TestSummaryFingerprint()
        {
            MemorySummaryFingerprintContribution first =
                Contribution("origin-rec-a", 0, "fact-kind", "pawn", "Pawn_B", "count_occurrences", "");
            first.contributionId = RequireContributionId(first);
            first.category = "personal";
            first.importance = "medium";
            MemorySummaryFingerprintContribution second =
                Contribution("origin-rec-b", 3, "opinion", "pawn", "Pawn_C", "int64_range", "-42");
            second.contributionId = RequireContributionId(second);
            second.category = "relationships";
            second.importance = "high";
            second.subjectRefIds.Add("5:pawn6:Pawn_B");
            second.provenanceRefIds.Add("7:capture_signal");

            List<string> bucketKeys = new List<string> { "bucket-a", "bucket-b" };
            string baseline;
            AssertTrue("summaryfp.canonical.create",
                MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                    1, bucketKeys, new[] { first, second }, out baseline));
            AssertTrue("summaryfp.canonical.sha-shape",
                baseline.Length == 64 && baseline == baseline.ToLowerInvariant());

            // Deterministic: identical inputs produce identical bytes.
            string repeat;
            MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                1, bucketKeys, new[] { first, second }, out repeat);
            AssertEqual("summaryfp.deterministic", baseline, repeat);

            // Every hashed field is order-sensitive: permutation changes the digest...
            string permutedContributions;
            MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                1, bucketKeys, new[] { second, first }, out permutedContributions);
            AssertTrue("summaryfp.contribution-order-sensitive", baseline != permutedContributions);
            string permutedBuckets;
            MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                1, new List<string> { "bucket-b", "bucket-a" }, new[] { first, second },
                out permutedBuckets);
            AssertTrue("summaryfp.bucket-order-sensitive", baseline != permutedBuckets);

            // ...and so is every scalar payload field.
            MemorySummaryFingerprintContribution changedValue =
                Contribution("origin-rec-b", 3, "opinion", "pawn", "Pawn_C", "int64_range", "-43");
            changedValue.contributionId = RequireContributionId(changedValue);
            changedValue.category = "relationships";
            changedValue.importance = "high";
            changedValue.subjectRefIds.Add("5:pawn6:Pawn_B");
            changedValue.provenanceRefIds.Add("7:capture_signal");
            string changedValueDigest;
            MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                1, bucketKeys, new[] { first, changedValue }, out changedValueDigest);
            AssertTrue("summaryfp.value-sensitive", baseline != changedValueDigest);

            string changedRevision;
            MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                2, bucketKeys, new[] { first, second }, out changedRevision);
            AssertTrue("summaryfp.reducer-sensitive", baseline != changedRevision);

            // Exclusions: originChapterId is placement metadata and is NOT part of this API at all
            // (compile-level exclusion); labels/wording have no input fields either.

            // Projection fingerprint: mask + format revision + filtered contribution set.
            string projection;
            AssertTrue("summaryfp.projection.create",
                MemorySummaryFingerprint.TryCreateProjectionFingerprint(
                    1, 1, 0b0011, new[] { first }, out projection));
            string projectionRepeat;
            MemorySummaryFingerprint.TryCreateProjectionFingerprint(
                1, 1, 0b0011, new[] { first }, out projectionRepeat);
            AssertEqual("summaryfp.projection.stable", projection, projectionRepeat);
            string projectionOtherMask;
            MemorySummaryFingerprint.TryCreateProjectionFingerprint(
                1, 1, 0b0001, new[] { first }, out projectionOtherMask);
            AssertTrue("summaryfp.projection.mask-sensitive", projection != projectionOtherMask);
            string projectionOtherFormat;
            MemorySummaryFingerprint.TryCreateProjectionFingerprint(
                1, 2, 0b0011, new[] { first }, out projectionOtherFormat);
            AssertTrue("summaryfp.projection.format-sensitive", projection != projectionOtherFormat);
            AssertTrue("summaryfp.projection.domain-distinct", projection != baseline);

            // Unknown category bits fail closed (only the four known low bits may be set).
            string refusedMask;
            AssertTrue("summaryfp.projection.unknown-mask-refused",
                !MemorySummaryFingerprint.TryCreateProjectionFingerprint(
                    1, 1, 0b10000, new[] { first }, out refusedMask));

            // Invalid inputs refuse instead of hashing garbage.
            string refused;
            AssertTrue("summaryfp.zero-reducer-refused",
                !MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                    0, bucketKeys, new[] { first }, out refused));
            MemorySummaryFingerprintContribution wrongId =
                Contribution("origin-rec-b", 3, "opinion", "pawn", "Pawn_C", "int64_range", "-42");
            wrongId.contributionId = RequireContributionId(wrongId) + "0";
            wrongId.category = "relationships";
            wrongId.importance = "high";
            AssertTrue("summaryfp.id-mismatch-refused",
                !MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                    1, bucketKeys, new[] { wrongId }, out refused));
            MemorySummaryFingerprintContribution badCategory =
                Contribution("origin-rec-b", 3, "opinion", "pawn", "Pawn_C", "int64_range", "-42");
            badCategory.contributionId = RequireContributionId(badCategory);
            badCategory.category = "weather";
            badCategory.importance = "high";
            AssertTrue("summaryfp.unknown-category-refused",
                !MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                    1, bucketKeys, new[] { badCategory }, out refused));
            MemorySummaryFingerprintContribution negativeTick =
                Contribution("origin-rec-b", 3, "opinion", "pawn", "Pawn_C", "int64_range", "-42");
            negativeTick.contributionId = RequireContributionId(negativeTick);
            negativeTick.category = "relationships";
            negativeTick.importance = "high";
            negativeTick.originalEventTick = -5;
            AssertTrue("summaryfp.negative-tick-without-ageunknown-refused",
                !MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                    1, bucketKeys, new[] { negativeTick }, out refused));
            MemorySummaryFingerprintContribution dupRefs =
                Contribution("origin-rec-b", 3, "opinion", "pawn", "Pawn_C", "int64_range", "-42");
            dupRefs.contributionId = RequireContributionId(dupRefs);
            dupRefs.category = "relationships";
            dupRefs.importance = "high";
            dupRefs.subjectRefIds.Add("5:pawn6:Pawn_B");
            dupRefs.subjectRefIds.Add("5:pawn6:Pawn_B");
            AssertTrue("summaryfp.duplicate-subject-ref-refused",
                !MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                    1, bucketKeys, new[] { dupRefs }, out refused));
            string duplicateBucket;
            AssertTrue("summaryfp.duplicate-bucket-refused",
                !MemorySummaryFingerprint.TryCreateCanonicalFactsFingerprint(
                    1, new List<string> { "bucket-a", "bucket-a" }, new[] { first },
                    out duplicateBucket));
        }

        private static void TestIdentityCarrierRegistry()
        {
            // Each carrier family is a sole high-water witness at the detached-token level:
            // envelope/root/block/awareness/guard/opportunity/request/audit tokens all arrive as
            // epoch-token strings here, reservations arrive structurally (§T13.2 carrier table).
            MemorySavedCarrierScanInput input = new MemorySavedCarrierScanInput
            {
                lastIssuedAutobiographicalEpochSequence = 5
            };
            input.epochTokenCarriers.Add(Epoch(9)); // a root/blocks/awareness witness beats 5
            MemorySavedCarrierRegistryPlan plan =
                MemorySavedIdentityCarrierRegistry.Plan(input);
            AssertTrue("carrier.plan.publishable", plan.canPublish);
            AssertEqual("carrier.normal-raises-high-water", 9L,
                plan.repairedAutobiographicalHighWater);
            AssertEqual("carrier.live-set.count", 1, plan.liveEpochTokens.Count);
            AssertTrue("carrier.chain-empty-normal-mode", !plan.fallbackModeForced);
            AssertTrue("carrier.no-repair-needed",
                !plan.invalidFallbackChainNeedsRepair
                && !plan.inconsistentFallbackRegistryNeedsRepair);

            // Malformed carriers are inert witnesses: counted, never parsed into identity.
            MemorySavedCarrierScanInput malformed = new MemorySavedCarrierScanInput
            {
                lastIssuedAutobiographicalEpochSequence = 5
            };
            malformed.epochTokenCarriers.Add("not-an-epoch");
            malformed.epochTokenCarriers.Add(Epoch(7));
            MemorySavedCarrierRegistryPlan malformedPlan =
                MemorySavedIdentityCarrierRegistry.Plan(malformed);
            AssertEqual("carrier.malformed-inert.high-water", 7L,
                malformedPlan.repairedAutobiographicalHighWater);
            AssertEqual("carrier.malformed-inert.counted", 1,
                malformedPlan.malformedEpochCarrierCount);
            AssertEqual("carrier.malformed-inert.live-set", 1,
                malformedPlan.liveEpochTokens.Count);

            // Permutation invariance: shuffled carriers produce an identical plan.
            MemorySavedCarrierScanInput permuted = new MemorySavedCarrierScanInput
            {
                lastIssuedAutobiographicalEpochSequence = 5
            };
            permuted.epochTokenCarriers.AddRange(new[] { Epoch(3), Epoch(9), EpochFallback(0) });
            permuted.legacyReservations.AddRange(new[]
            {
                new MemoryLegacyEpochReservationInput
                    { ownerPawnId = "Pawn_A", reservedEpochSequence = 12 },
                new MemoryLegacyEpochReservationInput
                    { ownerPawnId = "Pawn_B", reservedEpochSequence = 11 }
            });
            MemorySavedCarrierScanInput reversed = new MemorySavedCarrierScanInput
            {
                lastIssuedAutobiographicalEpochSequence = 5
            };
            reversed.epochTokenCarriers.AddRange(new[] { EpochFallback(0), Epoch(9), Epoch(3) });
            reversed.legacyReservations.Insert(0, new MemoryLegacyEpochReservationInput
            {
                ownerPawnId = "Pawn_B",
                reservedEpochSequence = 11
            });
            reversed.legacyReservations.Insert(0, new MemoryLegacyEpochReservationInput
            {
                ownerPawnId = "Pawn_A",
                reservedEpochSequence = 12
            });
            MemorySavedCarrierRegistryPlan permutedPlan =
                MemorySavedIdentityCarrierRegistry.Plan(permuted);
            MemorySavedCarrierRegistryPlan reversedPlan =
                MemorySavedIdentityCarrierRegistry.Plan(reversed);
            AssertTrue("carrier.permutation.tokens-equal",
                string.Join("|", permutedPlan.liveEpochTokens)
                == string.Join("|", reversedPlan.liveEpochTokens));
            AssertEqual("carrier.permutation.reservations-equal", 2,
                reversedPlan.normalizedReservations.Count);
            AssertEqual("carrier.permutation.reservation-first", "Pawn_A|12",
                reversedPlan.normalizedReservations[0].ownerPawnId + "|"
                + reversedPlan.normalizedReservations[0].reservedEpochSequence);
            AssertEqual("carrier.permutation.reservation-second", "Pawn_B|11",
                reversedPlan.normalizedReservations[1].ownerPawnId + "|"
                + reversedPlan.normalizedReservations[1].reservedEpochSequence);

            // A valid nonempty fallback chain forces permanent fallback mode at MaxValue.
            string chain = FallbackChainFixture("seed");
            MemorySavedCarrierScanInput forced = new MemorySavedCarrierScanInput
            {
                lastIssuedAutobiographicalEpochSequence = 5,
                lastIssuedAutobiographicalEpochFallbackChain = chain
            };
            forced.epochTokenCarriers.Add(Epoch(9));
            MemorySavedCarrierRegistryPlan forcedPlan =
                MemorySavedIdentityCarrierRegistry.Plan(forced);
            AssertTrue("carrier.forced-fallback.mode", forcedPlan.fallbackModeForced);
            AssertEqual("carrier.forced-fallback.max-value", long.MaxValue,
                forcedPlan.repairedAutobiographicalHighWater);
            AssertEqual("carrier.forced-fallback.chain-preserved", chain,
                forcedPlan.effectiveFallbackChain);

            // An invalid nonempty chain is repair-needed and never trusted.
            MemorySavedCarrierScanInput invalidChain = new MemorySavedCarrierScanInput
            {
                lastIssuedAutobiographicalEpochSequence = 5,
                lastIssuedAutobiographicalEpochFallbackChain = "NOT-A-HASH"
            };
            MemorySavedCarrierRegistryPlan invalidPlan =
                MemorySavedIdentityCarrierRegistry.Plan(invalidChain);
            AssertTrue("carrier.invalid-chain.flag", invalidPlan.invalidFallbackChainNeedsRepair);
            AssertEqual("carrier.invalid-chain.chain-cleared", string.Empty,
                invalidPlan.effectiveFallbackChain);
            AssertEqual("carrier.invalid-chain.high-water-still-raised", 5L,
                invalidPlan.repairedAutobiographicalHighWater);

            // Empty chain + live fallback carriers is the inconsistent-repair state.
            MemorySavedCarrierScanInput inconsistent = new MemorySavedCarrierScanInput
            {
                lastIssuedAutobiographicalEpochSequence = 5
            };
            inconsistent.epochTokenCarriers.Add(EpochFallback(2));
            MemorySavedCarrierRegistryPlan inconsistentPlan =
                MemorySavedIdentityCarrierRegistry.Plan(inconsistent);
            AssertTrue("carrier.inconsistent.flag",
                inconsistentPlan.inconsistentFallbackRegistryNeedsRepair);
            AssertTrue("carrier.inconsistent.token-in-live-set",
                inconsistentPlan.liveEpochTokens.Contains(EpochFallback(2)));

            // Reservation repair: owners visit ordinally; each keeps its lowest unclaimed sequence.
            MemorySavedCarrierScanInput reservations = new MemorySavedCarrierScanInput
            {
                lastIssuedAutobiographicalEpochSequence = 0
            };
            reservations.legacyReservations.AddRange(new[]
            {
                new MemoryLegacyEpochReservationInput
                    { ownerPawnId = "Pawn_B", reservedEpochSequence = 20 },
                new MemoryLegacyEpochReservationInput
                    { ownerPawnId = "Pawn_B", reservedEpochSequence = 7 },
                new MemoryLegacyEpochReservationInput
                    { ownerPawnId = "Pawn_A", reservedEpochSequence = 20 },
                new MemoryLegacyEpochReservationInput
                    { ownerPawnId = "Pawn_A", reservedEpochSequence = 3 }
            });
            MemorySavedCarrierRegistryPlan reservationPlan =
                MemorySavedIdentityCarrierRegistry.Plan(reservations);
            AssertEqual("carrier.reservation.rows", 2,
                reservationPlan.normalizedReservations.Count);
            AssertEqual("carrier.reservation.first-owner", "Pawn_A",
                reservationPlan.normalizedReservations[0].ownerPawnId);
            AssertEqual("carrier.reservation.first-sequence", 3L,
                reservationPlan.normalizedReservations[0].reservedEpochSequence);
            AssertEqual("carrier.reservation.second-owner", "Pawn_B",
                reservationPlan.normalizedReservations[1].ownerPawnId);
            AssertEqual("carrier.reservation.second-sequence", 7L,
                reservationPlan.normalizedReservations[1].reservedEpochSequence);
            AssertEqual("carrier.reservation.raise-high-water", 20L,
                reservationPlan.repairedAutobiographicalHighWater);
            AssertEqual("carrier.reservation.valid-collisions-counted", 2,
                reservationPlan.droppedReservationCount);

            // Invalid reservations drop syntactically and never lower the high-water.
            MemorySavedCarrierScanInput invalidReservation = new MemorySavedCarrierScanInput
            {
                lastIssuedAutobiographicalEpochSequence = 30
            };
            invalidReservation.legacyReservations.Add(new MemoryLegacyEpochReservationInput
            {
                ownerPawnId = "Pawn_A",
                reservedEpochSequence = 0
            });
            invalidReservation.legacyReservations.Add(new MemoryLegacyEpochReservationInput
            {
                ownerPawnId = " ",
                reservedEpochSequence = 99
            });
            MemorySavedCarrierRegistryPlan invalidReservationPlan =
                MemorySavedIdentityCarrierRegistry.Plan(invalidReservation);
            AssertEqual("carrier.reservation.invalid-dropped", 2,
                invalidReservationPlan.droppedReservationCount);
            AssertEqual("carrier.reservation.never-lowered", 30L,
                invalidReservationPlan.repairedAutobiographicalHighWater);
            AssertEqual("carrier.reservation.none-survive", 0,
                invalidReservationPlan.normalizedReservations.Count);

            // Faction generations raise monotonically; MaxValue is a typed saturation flag —
            // from the SAVED allocator alone, without any carrier repeating the value.
            MemorySavedCarrierScanInput saturatedSaved = new MemorySavedCarrierScanInput
            {
                globalFactionSnapshotAllocatorGeneration = long.MaxValue
            };
            MemorySavedCarrierRegistryPlan savedSaturatedPlan =
                MemorySavedIdentityCarrierRegistry.Plan(saturatedSaved);
            AssertTrue("carrier.faction.saved-max-saturated",
                savedSaturatedPlan.factionGenerationSaturated);
            MemorySavedCarrierScanInput factions = new MemorySavedCarrierScanInput
            {
                globalFactionSnapshotAllocatorGeneration = 4
            };
            factions.factionAllocatorGenerationCarriers.AddRange(new long[] { 2, 17, 9 });
            MemorySavedCarrierRegistryPlan factionPlan =
                MemorySavedIdentityCarrierRegistry.Plan(factions);
            AssertEqual("carrier.faction.raised", 17L,
                factionPlan.globalFactionAllocatorGeneration);
            AssertTrue("carrier.faction.not-saturated", !factionPlan.factionGenerationSaturated);
            MemorySavedCarrierScanInput saturatedFactions = new MemorySavedCarrierScanInput
            {
                globalFactionSnapshotAllocatorGeneration = 1
            };
            saturatedFactions.factionAllocatorGenerationCarriers.Add(long.MaxValue);
            MemorySavedCarrierRegistryPlan saturatedPlan =
                MemorySavedIdentityCarrierRegistry.Plan(saturatedFactions);
            AssertTrue("carrier.faction.saturated-flag",
                saturatedPlan.factionGenerationSaturated);
            AssertEqual("carrier.faction.saturated-no-wrap", long.MaxValue,
                saturatedPlan.globalFactionAllocatorGeneration);

            // High-water never lowers below the saved allocator value even without carriers.
            MemorySavedCarrierScanInput empty = new MemorySavedCarrierScanInput
            {
                lastIssuedAutobiographicalEpochSequence = 41
            };
            MemorySavedCarrierRegistryPlan emptyPlan =
                MemorySavedIdentityCarrierRegistry.Plan(empty);
            AssertEqual("carrier.empty.keeps-saved-high-water", 41L,
                emptyPlan.repairedAutobiographicalHighWater);
        }

        private static MemorySummaryFingerprintContribution Contribution(
            string originRecordId,
            int originFactOrdinal,
            string factKind,
            string subjectKind,
            string subjectId,
            string aggregation,
            string canonicalValue)
        {
            string factId;
            AssertTrue("summaryfp.helper.fact-id",
                MemoryIdentityCodec.TryCreateFactId(
                    "rule-x",
                    "disc-y",
                    factKind,
                    subjectKind,
                    subjectId,
                    aggregation,
                    out factId));
            return new MemorySummaryFingerprintContribution
            {
                originRecordId = originRecordId,
                originFactOrdinal = originFactOrdinal,
                originFactId = factId,
                canonicalValue = canonicalValue
            };
        }

        private static string RequireContributionId(
            MemorySummaryFingerprintContribution contribution)
        {
            string contributionId;
            AssertTrue("summaryfp.helper.contribution-id",
                MemoryIdentityCodec.TryCreateContributionId(
                    contribution.originRecordId,
                    contribution.originFactOrdinal,
                    contribution.originFactId,
                    out contributionId));
            return contributionId;
        }

        private static string EpochFallback(long probe)
        {
            return OrdinalSegmentCodec.Segment("memory-epoch-fallback-v1")
                + OrdinalSegmentCodec.Segment(new string('a', 64))
                + OrdinalSegmentCodec.Segment(
                    probe.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static string FallbackChainFixture(string seed)
        {
            using (System.Security.Cryptography.SHA256 sha =
                System.Security.Cryptography.SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(
                    System.Text.Encoding.UTF8.GetBytes("chain-fixture:" + seed));
                var builder = new System.Text.StringBuilder(digest.Length * 2);
                foreach (byte value in digest)
                {
                    builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static string KindToken(MemorySavedAtomKind kind)
        {
            switch (kind)
            {
                case MemorySavedAtomKind.Bool: return "bool";
                case MemorySavedAtomKind.Int32: return "int32";
                case MemorySavedAtomKind.Int64: return "int64";
                case MemorySavedAtomKind.String: return "string";
                case MemorySavedAtomKind.Row: return "row";
                case MemorySavedAtomKind.NullableRow: return "nullable_row";
                case MemorySavedAtomKind.List: return "list";
                default: throw new ArgumentOutOfRangeException("kind");
            }
        }

        private sealed class SyntheticStateFactRow : IMemoryLogicalSizeSource
        {
            public string factKey = string.Empty;

            public void CollectFields(MemoryLogicalSizeCollector collector)
            {
                collector.BeginRow("SavedMemoryStateFact");
                collector.Int32("schemaVersion", 1);
                collector.String("factKey", factKey);
                collector.String("factValue", string.Empty);
                collector.EndRow();
            }
        }

        private sealed class SyntheticAwarenessRow : IMemoryLogicalSizeSource
        {
            public string snapshotId = string.Empty;
            public bool hasFacts;
            public int factCount;
            public int factUnits;

            public void CollectFields(MemoryLogicalSizeCollector collector)
            {
                collector.BeginRow("SavedMemoryAwarenessSnapshot");
                collector.Int32("schemaVersion", 1);
                collector.String("snapshotId", snapshotId);
                collector.String("scopeKindToken", "relationship");
                collector.String("subjectKind", "pawn");
                collector.String("subjectId", "Pawn_A");
                collector.String("factStreamToken", "body_history");
                collector.Int64("captureInvalidationGeneration", 1);
                collector.String("knownnessEvidenceToken", "direct");
                collector.ListCount("stateFacts", hasFacts ? factCount : 0);
                for (int i = 0; hasFacts && i < factCount; i++)
                {
                    collector.NestedRow(new SyntheticStateFactRow());
                }

                collector.Int64("firstObservedTick", 0);
                collector.Int64("lastObservedTick", 0);
                collector.String("lastSourceOccurrenceId", string.Empty);
                collector.String("trackingStateToken", "tracked");
                collector.Int64("snapshotRevision", 1);
                collector.EndRow();
            }
        }

        private static void TestLogicalPayloadSizer()
        {
            // Exact golden bytes for one minimal registered row: 64 framing + 4 + (4+0) + (4+0).
            var minimal = new SyntheticStateFactRow();
            MemoryLogicalSizeResult minimalResult = MemoryLogicalPayloadSizer.Size(minimal);
            AssertTrue("sizer.minimal.valid", minimalResult.valid);
            AssertEqual("sizer.minimal.golden-bytes", 76L, minimalResult.totalBytes);

            // String charging is the exact UTF-8 byte count plus its 4-byte length prefix.
            // "Ж" is 2 UTF-16 units but 2 UTF-8 bytes; 😀 is 2 units, 4 UTF-8 bytes.
            var unicode = new SyntheticStateFactRow { factKey = "Ж😀" };
            MemoryLogicalSizeResult unicodeResult = MemoryLogicalPayloadSizer.Size(unicode);
            AssertTrue("sizer.unicode.valid", unicodeResult.valid);
            AssertEqual("sizer.unicode.golden-bytes", 76L - 4 + 4 + 6, unicodeResult.totalBytes);

            // Nullable presence: one byte when absent; presence byte plus nested row when present.
            long childBytes = minimalResult.totalBytes;
            MemoryLogicalSizeResult absent =
                MemoryLogicalPayloadSizer.SizeNullableSingleton("rollingSummaryBlock", null);
            AssertTrue("sizer.nullable-absent.valid", absent.valid);
            AssertEqual("sizer.nullable-absent.bytes", 1L, absent.totalBytes);
            MemoryLogicalSizeResult present =
                MemoryLogicalPayloadSizer.SizeNullableSingleton("rollingSummaryBlock", minimal);
            AssertTrue("sizer.nullable-present.valid", present.valid);
            AssertEqual("sizer.nullable-present.bytes", 1 + childBytes, present.totalBytes);

            // Deep-list charging: 4-byte count prefix plus one full nested row each.
            var list = new SyntheticAwarenessRow { hasFacts = true, factCount = 2 };
            MemoryLogicalSizeResult listResult = MemoryLogicalPayloadSizer.Size(list);
            AssertTrue("sizer.deep-list.valid", listResult.valid);
            // Awareness row framing+scalars: compute relative to one-fact variant below instead.
            var single = new SyntheticAwarenessRow { hasFacts = true, factCount = 1 };
            MemoryLogicalSizeResult singleResult = MemoryLogicalPayloadSizer.Size(single);
            AssertEqual("sizer.deep-list.delta-per-row", childBytes,
                listResult.totalBytes - singleResult.totalBytes);

            // Raw wrapper escape hatch: exact legacy walker bytes via UnregisteredRawBytes.
            // 64 framing + 4 + (4+5) + 4 + 4 + 4 + 1 + (4 prefix + 7 bytes) = 101.
            var rawProbe = new RawProbeRow { bytes = 7 };
            MemoryLogicalSizeResult rawResult = MemoryLogicalPayloadSizer.Size(rawProbe);
            AssertTrue("sizer.raw.valid", rawResult.valid);
            AssertEqual("sizer.raw.golden-bytes", 101L, rawResult.totalBytes);

            // Shape violations fail closed with a path, never throw through the caller.
            MemoryLogicalSizeResult wrongOrder =
                MemoryLogicalPayloadSizer.Size(new WrongOrderRow());
            AssertTrue("sizer.wrong-order.invalid", !wrongOrder.valid);
            AssertTrue("sizer.wrong-order.path",
                wrongOrder.errorPath.Contains("SavedMemoryStateFact"));
            MemoryLogicalSizeResult missingField =
                MemoryLogicalPayloadSizer.Size(new MissingFieldRow());
            AssertTrue("sizer.missing-field.invalid", !missingField.valid);
            MemoryLogicalSizeResult extraField = MemoryLogicalPayloadSizer.Size(new ExtraFieldRow());
            AssertTrue("sizer.extra-field.invalid", !extraField.valid);
            MemoryLogicalSizeResult badSurrogate =
                MemoryLogicalPayloadSizer.Size(new SurrogateRow());
            AssertTrue("sizer.surrogate.invalid", !badSurrogate.valid);
            MemoryLogicalSizeResult negativeCount =
                MemoryLogicalPayloadSizer.Size(new NegativeCountRow());
            AssertTrue("sizer.negative-count.invalid", !negativeCount.valid);
            MemoryLogicalSizeResult unregistered =
                MemoryLogicalPayloadSizer.Size(new UnregisteredRow());
            AssertTrue("sizer.unregistered-row.invalid", !unregistered.valid);

            // Null source is invalid input, not a crash.
            MemoryLogicalSizeResult nullResult = MemoryLogicalPayloadSizer.Size(null);
            AssertTrue("sizer.null-source.invalid", !nullResult.valid);
        }

        private sealed class RawProbeRow : IMemoryLogicalSizeSource
        {
            public long bytes = 7;

            public void CollectFields(MemoryLogicalSizeCollector collector)
            {
                collector.BeginRow("SavedLegacyUnresolvedOwnerArchiveInputV1");
                collector.Int32("schemaVersion", 1);
                collector.String("savedOwnerIdentityKindToken", "blank");
                collector.String("savedOwnerIdentityValue", string.Empty);
                collector.Int32("sourceContainerOrdinal", -1);
                collector.Int32("sourceRecordOrdinal", -1);
                collector.NullablePresence("legacyRecord", true);
                collector.UnregisteredRawBytes(bytes);
                collector.ClearPendingChild();
                collector.EndRow();
            }
        }

        private sealed class WrongOrderRow : IMemoryLogicalSizeSource
        {
            public void CollectFields(MemoryLogicalSizeCollector collector)
            {
                collector.BeginRow("SavedMemoryStateFact");
                collector.String("factKey", "x"); // registry expects schemaVersion first
                collector.EndRow();
            }
        }

        private sealed class MissingFieldRow : IMemoryLogicalSizeSource
        {
            public void CollectFields(MemoryLogicalSizeCollector collector)
            {
                collector.BeginRow("SavedMemoryStateFact");
                collector.Int32("schemaVersion", 1);
                collector.String("factKey", "x"); // never pushes factValue
                collector.EndRow();
            }
        }

        private sealed class ExtraFieldRow : IMemoryLogicalSizeSource
        {
            public void CollectFields(MemoryLogicalSizeCollector collector)
            {
                collector.BeginRow("SavedMemoryStateFact");
                collector.Int32("schemaVersion", 1);
                collector.String("factKey", "x");
                collector.String("factValue", "y");
                collector.Boolean("surprise", true); // not in the frozen schema
                collector.EndRow();
            }
        }

        private sealed class SurrogateRow : IMemoryLogicalSizeSource
        {
            public void CollectFields(MemoryLogicalSizeCollector collector)
            {
                collector.BeginRow("SavedMemoryStateFact");
                collector.Int32("schemaVersion", 1);
                collector.String("factKey", "\uD800"); // unpaired high surrogate
                collector.String("factValue", string.Empty);
                collector.EndRow();
            }
        }

        private sealed class NegativeCountRow : IMemoryLogicalSizeSource
        {
            public void CollectFields(MemoryLogicalSizeCollector collector)
            {
                collector.BeginRow("SavedMemoryAwarenessSnapshot");
                collector.Int32("schemaVersion", 1);
                collector.String("snapshotId", string.Empty);
                collector.String("scopeKindToken", string.Empty);
                collector.String("subjectKind", string.Empty);
                collector.String("subjectId", string.Empty);
                collector.String("factStreamToken", string.Empty);
                collector.Int64("captureInvalidationGeneration", 0);
                collector.String("knownnessEvidenceToken", string.Empty);
                collector.ListCount("stateFacts", -3);
                collector.EndRow();
            }
        }

        private sealed class UnregisteredRow : IMemoryLogicalSizeSource
        {
            public void CollectFields(MemoryLogicalSizeCollector collector)
            {
                collector.BeginRow("NotAFrozenRow");
                collector.EndRow();
            }
        }

        private static void TestActivePayloadBudget()
        {
            MemoryBudgetLimits limits = new MemoryBudgetLimits
            {
                activeOwnerBytes = 1000,
                combinedOwnerBytes = 1200,
                activeGlobalBytes = 5000,
                combinedGlobalBytes = 6000
            };
            var globals = new MemoryPayloadBudgetTotals
            {
                globalActiveBytes = 3000,
                globalImportedBytes = 1000
            };

            MemoryBudgetDecision admit = ActiveMemoryPayloadBudget.TryAdmit(
                limits, ownerActiveBytesCurrent: 400, ownerImportedBytesCurrent: 100,
                ownerDeltaActive: 300, ownerDeltaImported: 50, globalCurrent: globals);
            AssertEqual("budget.admit.outcome", "admitted", admit.OutcomeToken());
            AssertEqual("budget.admit.owner-active", 700L, admit.newOwnerActiveBytes);
            AssertEqual("budget.admit.owner-imported", 150L, admit.newOwnerImportedBytes);
            AssertEqual("budget.admit.global-active", 3300L, admit.newTotals.globalActiveBytes);
            AssertEqual("budget.admit.global-imported", 1050L, admit.newTotals.globalImportedBytes);

            // Owner active cap refuses without touching anything.
            MemoryBudgetDecision ownerFull = ActiveMemoryPayloadBudget.TryAdmit(
                limits, 900, 0, 200, 0, globals);
            AssertEqual("budget.owner-active-full",
                "owner_active_full", ownerFull.OutcomeToken());

            // Owner combined (active + imported) cap refuses.
            MemoryBudgetDecision ownerCombinedFull = ActiveMemoryPayloadBudget.TryAdmit(
                limits, 900, 250, 100, 0, globals);
            AssertEqual("budget.owner-combined-full",
                "owner_combined_full", ownerCombinedFull.OutcomeToken());

            // Global active and combined caps refuse independently of the per-owner caps.
            var nearFullGlobals = new MemoryPayloadBudgetTotals
            {
                globalActiveBytes = 4500,
                globalImportedBytes = 1000
            };
            MemoryBudgetDecision globalActiveFull = ActiveMemoryPayloadBudget.TryAdmit(
                limits, 0, 0, 700, 0, nearFullGlobals);
            AssertEqual("budget.global-active-full",
                "global_active_full", globalActiveFull.OutcomeToken());
            MemoryBudgetDecision globalCombinedFull = ActiveMemoryPayloadBudget.TryAdmit(
                limits, 0, 0, 200, 900, nearFullGlobals);
            AssertEqual("budget.global-combined-full",
                "global_combined_full", globalCombinedFull.OutcomeToken());

            // Negative deltas (expiry/removal) are legal and can move totals downward.
            MemoryBudgetDecision shrink = ActiveMemoryPayloadBudget.TryAdmit(
                limits, 400, 100, -300, -100, globals);
            AssertEqual("budget.shrink.outcome", "admitted", shrink.OutcomeToken());
            AssertEqual("budget.shrink.owner-active", 100L, shrink.newOwnerActiveBytes);
            AssertEqual("budget.shrink.global-imported", 900L,
                shrink.newTotals.globalImportedBytes);

            // Invalid inputs fail closed.
            MemoryBudgetDecision negativeCurrent = ActiveMemoryPayloadBudget.TryAdmit(
                limits, -1, 0, 10, 0, globals);
            AssertEqual("budget.negative-current", "invalid", negativeCurrent.OutcomeToken());
            MemoryBudgetDecision badLimits = ActiveMemoryPayloadBudget.TryAdmit(
                default(MemoryBudgetLimits), 0, 0, 10, 0, globals);
            AssertEqual("budget.bad-limits", "invalid", badLimits.OutcomeToken());
            // A removal larger than current totals would go negative: invalid, never admitted.
            MemoryBudgetDecision negativeAfterDelta = ActiveMemoryPayloadBudget.TryAdmit(
                limits, 100, 0, -400, 0, globals);
            AssertEqual("budget.negative-after-delta", "invalid", negativeAfterDelta.OutcomeToken());
            MemoryBudgetDecision overflowDelta = ActiveMemoryPayloadBudget.TryAdmit(
                limits, long.MaxValue - 5, 0, 100, 0, globals);
            AssertEqual("budget.overflow", "invalid", overflowDelta.OutcomeToken());
        }

        private static MemoryLegacyRuleMapEntry RuleMapEntry(string eventKind, string ruleId)
        {
            var entry = new MemoryLegacyRuleMapEntry
            {
                eventKind = eventKind,
                captureRuleId = ruleId,
                memoryKind = "event",
                category = "relationships",
                baseImportance = "medium"
            };
            var descriptor = new MemoryFactDescriptor
            {
                factKind = "relation",
                contextKey = "other",
                aggregationToken = "latest_state",
                canonicalValueKind = "state"
            };
            descriptor.allowedStates.Add("spouse");
            entry.factDescriptors.Add(descriptor);
            return entry;
        }

        private static MemoryLegacyRecordSnapshot LegacyRecord(
            string recordId,
            string dedupKey,
            string sourceEventId,
            string eventKind)
        {
            return new MemoryLegacyRecordSnapshot
            {
                recordId = recordId,
                dedupKey = dedupKey,
                sourceEventId = sourceEventId,
                eventKind = eventKind,
                topicKey = "relationship",
                tick = 5000,
                participantIds = new List<string> { "Pawn_B" },
                factKeys = new List<string> { "other" },
                factValues = new List<string> { "spouse" }
            };
        }

        private static MemoryLegacyRecordSnapshot CloneLegacyRecord(
            MemoryLegacyRecordSnapshot source)
        {
            var copy = new MemoryLegacyRecordSnapshot
            {
                recordId = source.recordId,
                dedupKey = source.dedupKey,
                sourceEventId = source.sourceEventId,
                sourceKind = source.sourceKind,
                recallScope = source.recallScope,
                eventKind = source.eventKind,
                topicKey = source.topicKey,
                tick = source.tick,
                dateLabel = source.dateLabel,
                fallbackSummary = source.fallbackSummary,
                manualTextOverride = source.manualTextOverride
            };
            copy.participantIds.AddRange(source.participantIds ?? new List<string>());
            copy.participantNames.AddRange(source.participantNames ?? new List<string>());
            copy.subjectKeys.AddRange(source.subjectKeys ?? new List<string>());
            copy.factKeys.AddRange(source.factKeys ?? new List<string>());
            copy.factValues.AddRange(source.factValues ?? new List<string>());
            return copy;
        }

        private static MemoryLegacyMappedRecord CloneMappedRecord(
            MemoryLegacyMappedRecord source)
        {
            var copy = new MemoryLegacyMappedRecord
            {
                disposition = source.disposition,
                sourceOccurrenceId = source.sourceOccurrenceId,
                captureRuleId = source.captureRuleId,
                factDiscriminator = source.factDiscriminator,
                kindToken = source.kindToken,
                categoryToken = source.categoryToken,
                importanceToken = source.importanceToken,
                originalEventTick = source.originalEventTick,
                ageUnknown = source.ageUnknown,
                playerEdited = source.playerEdited,
                suppressed = source.suppressed,
                provenanceRefId = source.provenanceRefId,
                importedWording = source.importedWording,
                originRecordId = source.originRecordId,
                dedupKey = source.dedupKey,
                originSourceEventId = source.originSourceEventId,
                sourceKind = source.sourceKind,
                recallScope = source.recallScope,
                eventKind = source.eventKind,
                topicKey = source.topicKey,
                dateLabel = source.dateLabel,
                fallbackSummary = source.fallbackSummary,
                backgroundText = source.backgroundText
            };
            copy.participantIds.AddRange(source.participantIds ?? new List<string>());
            copy.participantNames.AddRange(source.participantNames ?? new List<string>());
            copy.subjectKeys.AddRange(source.subjectKeys ?? new List<string>());
            copy.factKeys.AddRange(source.factKeys ?? new List<string>());
            copy.factValues.AddRange(source.factValues ?? new List<string>());
            foreach (MemoryLegacyMappedFact fact
                in source.facts ?? new List<MemoryLegacyMappedFact>())
            {
                copy.facts.Add(new MemoryLegacyMappedFact
                {
                    originFactOrdinal = fact.originFactOrdinal,
                    factId = fact.factId,
                    factKind = fact.factKind,
                    canonicalSubjectKind = fact.canonicalSubjectKind,
                    canonicalSubjectId = fact.canonicalSubjectId,
                    aggregationToken = fact.aggregationToken,
                    canonicalValueKind = fact.canonicalValueKind,
                    canonicalValue = fact.canonicalValue
                });
            }

            return copy;
        }

        private static void AssertMigrationReportsEqual(
            string label,
            MemoryLegacyMigrationReport expected,
            MemoryLegacyMigrationReport actual)
        {
            AssertEqual(label + ".owner", expected.ownerPawnId, actual.ownerPawnId);
            AssertEqual(label + ".epoch", expected.ownerEpochToken, actual.ownerEpochToken);
            AssertEqual(label + ".max-tick", expected.maxKnownTick, actual.maxKnownTick);
            AssertEqual(label + ".raw", expected.ownerRemainsRaw, actual.ownerRemainsRaw);
            AssertEqual(label + ".drops", expected.droppedAutomaticAlternateCount,
                actual.droppedAutomaticAlternateCount);
            AssertEqual(label + ".archives", expected.archivedAuthoredConflictCount,
                actual.archivedAuthoredConflictCount);
            AssertEqual(label + ".unmapped", expected.unmappedEventKindCount,
                actual.unmappedEventKindCount);
            AssertEqual(label + ".invalid-facts", expected.invalidFactValueCount,
                actual.invalidFactValueCount);
            AssertEqual(label + ".row-count", expected.rows.Count, actual.rows.Count);
            for (int i = 0; i < expected.rows.Count; i++)
            {
                AssertMappedRowsEqual(label + ".row-" + i, expected.rows[i], actual.rows[i]);
            }
        }

        private static void AssertMappedRowsEqual(
            string label,
            MemoryLegacyMappedRecord expected,
            MemoryLegacyMappedRecord actual)
        {
            AssertEqual(label + ".disposition", expected.disposition, actual.disposition);
            AssertEqual(label + ".occurrence", expected.sourceOccurrenceId, actual.sourceOccurrenceId);
            AssertEqual(label + ".rule", expected.captureRuleId, actual.captureRuleId);
            AssertEqual(label + ".discriminator", expected.factDiscriminator, actual.factDiscriminator);
            AssertEqual(label + ".kind", expected.kindToken, actual.kindToken);
            AssertEqual(label + ".category", expected.categoryToken, actual.categoryToken);
            AssertEqual(label + ".importance", expected.importanceToken, actual.importanceToken);
            AssertEqual(label + ".tick", expected.originalEventTick, actual.originalEventTick);
            AssertEqual(label + ".age", expected.ageUnknown, actual.ageUnknown);
            AssertEqual(label + ".edited", expected.playerEdited, actual.playerEdited);
            AssertEqual(label + ".suppressed", expected.suppressed, actual.suppressed);
            AssertEqual(label + ".provenance", expected.provenanceRefId, actual.provenanceRefId);
            AssertEqual(label + ".wording", expected.importedWording, actual.importedWording);
            AssertEqual(label + ".origin-record", expected.originRecordId, actual.originRecordId);
            AssertEqual(label + ".dedup", expected.dedupKey, actual.dedupKey);
            AssertEqual(label + ".source-event", expected.originSourceEventId,
                actual.originSourceEventId);
            AssertEqual(label + ".source-kind", expected.sourceKind, actual.sourceKind);
            AssertEqual(label + ".recall-scope", expected.recallScope, actual.recallScope);
            AssertEqual(label + ".event-kind", expected.eventKind, actual.eventKind);
            AssertEqual(label + ".topic", expected.topicKey, actual.topicKey);
            AssertEqual(label + ".date", expected.dateLabel, actual.dateLabel);
            AssertEqual(label + ".fallback", expected.fallbackSummary, actual.fallbackSummary);
            AssertStringListsEqual(label + ".participant-ids", expected.participantIds,
                actual.participantIds);
            AssertStringListsEqual(label + ".participant-names", expected.participantNames,
                actual.participantNames);
            AssertStringListsEqual(label + ".subjects", expected.subjectKeys, actual.subjectKeys);
            AssertStringListsEqual(label + ".fact-keys", expected.factKeys, actual.factKeys);
            AssertStringListsEqual(label + ".fact-values", expected.factValues, actual.factValues);
            AssertEqual(label + ".background", expected.backgroundText, actual.backgroundText);
            AssertEqual(label + ".fact-count", expected.facts.Count, actual.facts.Count);
            for (int i = 0; i < expected.facts.Count; i++)
            {
                MemoryLegacyMappedFact left = expected.facts[i];
                MemoryLegacyMappedFact right = actual.facts[i];
                AssertEqual(label + ".fact-ordinal-" + i,
                    left.originFactOrdinal, right.originFactOrdinal);
                AssertEqual(label + ".fact-id-" + i, left.factId, right.factId);
                AssertEqual(label + ".fact-kind-" + i, left.factKind, right.factKind);
                AssertEqual(label + ".fact-subject-kind-" + i,
                    left.canonicalSubjectKind, right.canonicalSubjectKind);
                AssertEqual(label + ".fact-subject-id-" + i,
                    left.canonicalSubjectId, right.canonicalSubjectId);
                AssertEqual(label + ".fact-aggregation-" + i,
                    left.aggregationToken, right.aggregationToken);
                AssertEqual(label + ".fact-value-kind-" + i,
                    left.canonicalValueKind, right.canonicalValueKind);
                AssertEqual(label + ".fact-value-" + i,
                    left.canonicalValue, right.canonicalValue);
            }
        }

        private static void AssertStringListsEqual(
            string label, List<string> expected, List<string> actual)
        {
            int expectedCount = expected?.Count ?? 0;
            int actualCount = actual?.Count ?? 0;
            AssertEqual(label + ".count", expectedCount, actualCount);
            for (int i = 0; i < expectedCount; i++)
            {
                AssertEqual(label + ".item-" + i, expected[i], actual[i]);
            }
        }

        private static void AssertAuthoredLegacyFieldDifference(
            string label,
            Action<MemoryLegacyRecordSnapshot> mutate,
            Func<MemoryLegacyMappedRecord, string> reportValue)
        {
            MemoryLegacyRecordSnapshot baseline =
                LegacyRecord("rec-field", "dedup-field", "evt-field", "relation.spouse.gained");
            baseline.manualTextOverride = "authored wording";
            baseline.dateLabel = "date-a";
            baseline.fallbackSummary = "fallback-a";
            baseline.participantNames.Add("B");
            MemoryLegacyRecordSnapshot changed = CloneLegacyRecord(baseline);
            mutate(changed);

            MemoryLegacyRuleMapEntry fieldRule =
                RuleMapEntry("relation.spouse.gained", "MarriageRule");
            fieldRule.factDescriptors[0].allowedStates.Add("friend");
            var alternateKeyDescriptor = new MemoryFactDescriptor
            {
                factKind = "relation",
                contextKey = "other-two",
                aggregationToken = "latest_state",
                canonicalValueKind = "state"
            };
            alternateKeyDescriptor.allowedStates.Add("spouse");
            alternateKeyDescriptor.allowedStates.Add("friend");
            fieldRule.factDescriptors.Add(alternateKeyDescriptor);

            var pair = new MemoryLegacyOwnerMigrationInput
            {
                ownerPawnId = "Pawn_Field",
                ruleMap = new List<MemoryLegacyRuleMapEntry>
                {
                    fieldRule
                },
                records = new List<MemoryLegacyRecordSnapshot> { baseline, changed }
            };
            MemoryLegacyMigrationReport pairReport =
                MemoryThreadMigrationPolicy.PlanDryRun(pair);
            AssertTrue(label + ".owner-current", !pairReport.ownerRemainsRaw);
            AssertEqual(label + ".rows", 2, pairReport.rows.Count);
            AssertEqual(label + ".archive", 1, pairReport.archivedAuthoredConflictCount);
            AssertTrue(label + ".both-values",
                !string.Equals(
                    reportValue(pairReport.rows[0]),
                    reportValue(pairReport.rows[1]),
                    StringComparison.Ordinal));

            var baselineOnly = new MemoryLegacyOwnerMigrationInput
            {
                ownerPawnId = "Pawn_Field",
                ruleMap = pair.ruleMap,
                records = new List<MemoryLegacyRecordSnapshot> { CloneLegacyRecord(baseline) }
            };
            var changedOnly = new MemoryLegacyOwnerMigrationInput
            {
                ownerPawnId = "Pawn_Field",
                ruleMap = pair.ruleMap,
                records = new List<MemoryLegacyRecordSnapshot> { CloneLegacyRecord(changed) }
            };
            AssertTrue(label + ".fingerprint-distinct",
                !string.Equals(
                    MemoryThreadMigrationPolicy.PlanDryRun(baselineOnly).reportFingerprint,
                    MemoryThreadMigrationPolicy.PlanDryRun(changedOnly).reportFingerprint,
                    StringComparison.Ordinal));
        }

        private static void AssertMappedReportFieldChangesIdentity(
            string label, Action<MemoryLegacyMappedRecord> mutate)
        {
            var baseline = new MemoryLegacyMappedRecord
            {
                disposition = MemoryLegacyMappedRecord.DispositionArchiveAuthored,
                sourceOccurrenceId = "occurrence",
                captureRuleId = "rule",
                factDiscriminator = "discriminator",
                kindToken = "event",
                categoryToken = "personal",
                importanceToken = "medium",
                originalEventTick = 12,
                playerEdited = true,
                provenanceRefId = "provenance",
                importedWording = "wording",
                originRecordId = "record",
                dedupKey = "dedup",
                originSourceEventId = "source-event",
                sourceKind = "captured",
                recallScope = "contextual",
                eventKind = "event-kind",
                topicKey = "topic",
                dateLabel = "date",
                fallbackSummary = "fallback",
                backgroundText = "background"
            };
            baseline.participantIds.Add("Pawn_A");
            baseline.participantNames.Add("A");
            baseline.subjectKeys.Add("subject");
            baseline.factKeys.Add("fact-key");
            baseline.factValues.Add("fact-value");
            baseline.facts.Add(new MemoryLegacyMappedFact
            {
                originFactOrdinal = 0,
                factId = "fact-id",
                factKind = "fact-kind",
                canonicalSubjectKind = "stream",
                canonicalSubjectId = "subject-id",
                aggregationToken = "latest-state",
                canonicalValueKind = "state",
                canonicalValue = "value"
            });
            MemoryLegacyMappedRecord changed = CloneMappedRecord(baseline);
            mutate(changed);

            AssertTrue(label + ".canonical-encoding",
                !string.Equals(
                    MemoryThreadMigrationPolicy.CanonicalMappedRecordEncoding(baseline),
                    MemoryThreadMigrationPolicy.CanonicalMappedRecordEncoding(changed),
                    StringComparison.Ordinal));
            AssertTrue(label + ".compare", MemoryThreadMigrationPolicy.CompareRows(
                baseline, changed) != 0);
            var baselineReport = new MemoryLegacyMigrationReport
            {
                ownerPawnId = "Pawn_Report",
                rows = new List<MemoryLegacyMappedRecord> { baseline }
            };
            var changedReport = new MemoryLegacyMigrationReport
            {
                ownerPawnId = "Pawn_Report",
                rows = new List<MemoryLegacyMappedRecord> { changed }
            };
            AssertTrue(label + ".fingerprint",
                !string.Equals(
                    MemoryThreadMigrationPolicy.Fingerprint(baselineReport),
                    MemoryThreadMigrationPolicy.Fingerprint(changedReport),
                    StringComparison.Ordinal));
        }

        private static void TestLegacyMigrationDryRun()
        {
            var input = new MemoryLegacyOwnerMigrationInput
            {
                ownerPawnId = "Pawn_A",
                ownerEpochToken = Epoch(1),
                ruleMap = new List<MemoryLegacyRuleMapEntry>
                {
                    RuleMapEntry("relation.spouse.gained", "MarriageRule")
                }
            };
            // One authored row plus one byte-identical automatic duplicate of another occurrence.
            var authored = LegacyRecord("rec-1", "dedup-1", "evt-9", "relation.spouse.gained");
            authored.manualTextOverride = "Our wedding, in my words.";
            input.records.Add(authored);
            // An irreconcilable second AUTHORING of the SAME occurrence archives as an alternate.
            var authoredAlternate =
                LegacyRecord("rec-1b", "dedup-1b", "evt-9", "relation.spouse.gained");
            authoredAlternate.manualTextOverride = "A different telling of our wedding.";
            input.records.Add(authoredAlternate);
            MemoryLegacyRecordSnapshot automatic =
                LegacyRecord("rec-2", "dedup-2", "evt-8", "relation.spouse.gained");
            input.records.Add(automatic);
            input.records.Add(CloneLegacyRecord(automatic));
            // A CONFLICTING unedited automatic alternate under the same occurrence drops.
            var conflictingAuto =
                LegacyRecord("rec-4", "dedup-4", "evt-8", "relation.spouse.gained");
            conflictingAuto.factValues = new List<string> { "fiance" };
            input.records.Add(conflictingAuto);

            MemoryLegacyMigrationReport report =
                MemoryThreadMigrationPolicy.PlanDryRun(input);
            AssertTrue("migration.report.valid",
                report != null && !report.ownerRemainsRaw);
            // Two active winners (evt-8 + evt-9) plus one archived authored alternate row.
            AssertEqual("migration.report.rows", 3, report.rows.Count);
            int activeWinners = 0;
            foreach (MemoryLegacyMappedRecord reportRow in report.rows)
            {
                if (reportRow.disposition == MemoryLegacyMappedRecord.DispositionActive)
                {
                    activeWinners++;
                }
            }
            AssertEqual("migration.active-winners", 2, activeWinners);
            // Byte-equal duplicates collapse silently; only a conflicting unedited
            // alternate counts as a bounded drop (§T8.4 step 5).
            AssertEqual("migration.automatic-duplicate-dropped", 1,
                report.droppedAutomaticAlternateCount);

            // Occurrence identity precedence: the valid sourceEventId IS the occurrence id.
            MemoryLegacyMappedRecord first = report.rows.First(row =>
                row.disposition == MemoryLegacyMappedRecord.DispositionActive
                && row.sourceOccurrenceId == "evt-8");
            AssertEqual("migration.occurrence-arm-source-event", "evt-8",
                first.sourceOccurrenceId);
            AssertEqual("migration.rule-id-from-map", "MarriageRule", first.captureRuleId);
            AssertEqual("migration.kind-from-map", "event", first.kindToken);
            AssertEqual("migration.importance-from-map", "medium", first.importanceToken);
            AssertTrue("migration.tick-known", !first.ageUnknown);
            AssertEqual("migration.original-tick-preserved", 5000L, first.originalEventTick);

            // Canonical fact reconstruction through the rule descriptor.
            AssertEqual("migration.fact-count", 1, first.facts.Count);
            if (first.facts.Count == 1)
            {
                AssertEqual("migration.fact-value", "spouse", first.facts[0].canonicalValue);
                AssertEqual("migration.fact-ordinal-zero-based", 0,
                    first.facts[0].originFactOrdinal);
                AssertTrue("migration.fact-id-framed",
                    first.facts[0].factId.Contains(":MarriageRule"));
                AssertTrue("migration.provenance-framed",
                    first.provenanceRefId.StartsWith("24:memory-provenance-ref-v1",
                        StringComparison.Ordinal));
            }

            // Authored alternate archives rather than becoming a second active identity.
            bool hasArchiveRow = false;
            foreach (MemoryLegacyMappedRecord row in report.rows)
            {
                if (row.disposition == MemoryLegacyMappedRecord.DispositionArchiveAuthored)
                {
                    hasArchiveRow = true;
                }
            }
            AssertTrue("migration.authored-conflict-present", hasArchiveRow);

            // Idempotence: an equal rerun is fingerprint-identical (§T13.5 fixtures).
            MemoryLegacyMigrationReport repeat =
                MemoryThreadMigrationPolicy.PlanDryRun(input);
            AssertTrue("migration.idempotent-rerun", report.IsIdempotentWith(repeat));

            // Unknown/removed-mod kind: conservative Landmark/Important with a generic rule id.
            var unknownInput = new MemoryLegacyOwnerMigrationInput
            {
                ownerPawnId = "Pawn_A",
                records = new List<MemoryLegacyRecordSnapshot>
                {
                    LegacyRecord("rec-x", "dedup-x", "", "removedmod.kind")
                }
            };
            MemoryLegacyMigrationReport unknownReport =
                MemoryThreadMigrationPolicy.PlanDryRun(unknownInput);
            AssertEqual("migration.unknown-kind.count", 1, unknownReport.unmappedEventKindCount);
            MemoryLegacyMappedRecord unknownRow = unknownReport.rows[0];
            AssertEqual("migration.unknown-kind.landmark", "landmark", unknownRow.kindToken);
            AssertEqual("migration.unknown-kind.important", "high", unknownRow.importanceToken);
            AssertTrue("migration.unknown-kind.generic-rule-framed",
                unknownRow.captureRuleId.Contains("memory-legacy-capture-rule-v1"));
            AssertTrue("migration.discriminator-framed",
                unknownRow.factDiscriminator.Contains("memory-legacy-fact-discriminator-v1"));

            // Missing/zero tick becomes ageUnknown Important — never a guessed date (§T15.2).
            var corruptTick = LegacyRecord("rec-t", "dedup-t", "evt-t", "relation.spouse.gained");
            corruptTick.tick = 0;
            var corruptInput = new MemoryLegacyOwnerMigrationInput
            {
                ownerPawnId = "Pawn_A",
                ruleMap = new List<MemoryLegacyRuleMapEntry>
                {
                    RuleMapEntry("relation.spouse.gained", "MarriageRule")
                },
                records = new List<MemoryLegacyRecordSnapshot> { corruptTick }
            };
            MemoryLegacyMigrationReport corruptReport =
                MemoryThreadMigrationPolicy.PlanDryRun(corruptInput);
            AssertTrue("migration.corrupt-tick.age-unknown",
                corruptReport.rows[0].ageUnknown);
            AssertEqual("migration.corrupt-tick.upgraded-important", "high",
                corruptReport.rows[0].importanceToken);

            // Identity never depends on input position: an exact REVERSAL of the same row
            // multiset produces the fingerprint-identical plan (§T13.5 permutation fixtures).
            var permuted = new MemoryLegacyOwnerMigrationInput
            {
                ownerPawnId = "Pawn_A",
                ownerEpochToken = Epoch(1),
                ruleMap = input.ruleMap
            };
            for (int i = input.records.Count - 1; i >= 0; i--)
            {
                permuted.records.Add(CloneLegacyRecord(input.records[i]));
            }

            MemoryLegacyMigrationReport permutedReport =
                MemoryThreadMigrationPolicy.PlanDryRun(permuted);
            AssertMigrationReportsEqual("migration.permutation.full", report, permutedReport);
            AssertEqual("migration.permutation.fingerprint",
                report.reportFingerprint, permutedReport.reportFingerprint);

            // Every formerly omitted preserved field must prevent authored collapse and must alter
            // the complete report fingerprint when it is the only input difference.
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.source-kind",
                row => row.sourceKind = "future-source",
                row => row.sourceKind);
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.recall-scope",
                row => row.recallScope = "future-scope",
                row => row.recallScope);
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.participant-id",
                row => row.participantIds[0] = "Pawn_C",
                row => string.Join("\u001f", row.participantIds));
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.participant-name",
                row => row.participantNames[0] = "C",
                row => string.Join("\u001f", row.participantNames));
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.date-label",
                row => row.dateLabel = "date-b",
                row => row.dateLabel);
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.fallback",
                row => row.fallbackSummary = "fallback-b",
                row => row.fallbackSummary);
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.origin-record",
                row => row.recordId = "rec-field-2",
                row => row.originRecordId);
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.dedup",
                row => row.dedupKey = "dedup-field-2",
                row => row.dedupKey);
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.event-kind",
                row => row.eventKind = "removed-mod-event-kind",
                row => row.eventKind);
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.topic",
                row => row.topicKey = "topic-b",
                row => row.topicKey);
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.tick",
                row => row.tick = 5001,
                row => row.originalEventTick.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.subject-list",
                row => row.subjectKeys.Add("part:Heart"),
                row => string.Join("\u001f", row.subjectKeys));
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.fact-key-list",
                row => row.factKeys[0] = "other-two",
                row => string.Join("\u001f", row.factKeys));
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.fact-value-list",
                row => row.factValues[0] = "friend",
                row => string.Join("\u001f", row.factValues));
            AssertAuthoredLegacyFieldDifference(
                "migration.authored.manual-wording",
                row => row.manualTextOverride = "second authored wording",
                row => row.importedWording);

            // CompareRows and Fingerprint previously used different incomplete field subsets.
            // Exercise every field that either old path omitted, with no second row difference.
            AssertMappedReportFieldChangesIdentity(
                "migration.report.dedup", row => row.dedupKey = "dedup-2");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.source-event",
                row => row.originSourceEventId = "source-event-2");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.source-kind", row => row.sourceKind = "player");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.recall-scope", row => row.recallScope = "full");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.event-kind", row => row.eventKind = "event-kind-2");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.topic", row => row.topicKey = "topic-2");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.date", row => row.dateLabel = "date-2");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.fallback", row => row.fallbackSummary = "fallback-2");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.participant-id", row => row.participantIds[0] = "Pawn_B");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.participant-name", row => row.participantNames[0] = "B");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.subject-list", row => row.subjectKeys[0] = "subject-2");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.fact-key-list", row => row.factKeys[0] = "fact-key-2");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.fact-value-list", row => row.factValues[0] = "fact-value-2");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.background", row => row.backgroundText = "background-2");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.fact-kind", row => row.facts[0].factKind = "fact-kind-2");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.fact-subject-kind",
                row => row.facts[0].canonicalSubjectKind = "pawn");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.fact-subject-id",
                row => row.facts[0].canonicalSubjectId = "subject-id-2");
            AssertMappedReportFieldChangesIdentity(
                "migration.report.fact-aggregation",
                row => row.facts[0].aggregationToken = "ordinal-set");

            // Structural safety is validated before winner selection. These two shapes cover the
            // old evasion path (malformed automatic alternate loses to authored winner) and the
            // obvious malformed-winner path, in both input permutations.
            MemoryLegacyRecordSnapshot validAuthored =
                LegacyRecord("rec-valid", "dedup-valid", "evt-shape", "relation.spouse.gained");
            validAuthored.manualTextOverride = "valid authored";
            MemoryLegacyRecordSnapshot malformedAlternate =
                LegacyRecord("rec-malformed", "dedup-malformed", "evt-shape", "relation.spouse.gained");
            malformedAlternate.factValues.Clear();
            var unsafeAlternate = new MemoryLegacyOwnerMigrationInput
            {
                ownerPawnId = "Pawn_Shape",
                ruleMap = input.ruleMap,
                records = new List<MemoryLegacyRecordSnapshot>
                    { validAuthored, malformedAlternate }
            };
            var unsafeAlternateReversed = new MemoryLegacyOwnerMigrationInput
            {
                ownerPawnId = "Pawn_Shape",
                ruleMap = input.ruleMap,
                records = new List<MemoryLegacyRecordSnapshot>
                    { CloneLegacyRecord(malformedAlternate), CloneLegacyRecord(validAuthored) }
            };
            MemoryLegacyMigrationReport unsafeReport =
                MemoryThreadMigrationPolicy.PlanDryRun(unsafeAlternate);
            MemoryLegacyMigrationReport unsafeReversedReport =
                MemoryThreadMigrationPolicy.PlanDryRun(unsafeAlternateReversed);
            AssertTrue("migration.unsafe-alternate.owner-raw", unsafeReport.ownerRemainsRaw);
            AssertMigrationReportsEqual(
                "migration.unsafe-alternate.permutation", unsafeReport, unsafeReversedReport);

            MemoryLegacyRecordSnapshot malformedWinner = CloneLegacyRecord(malformedAlternate);
            malformedWinner.manualTextOverride = "authored malformed winner";
            MemoryLegacyRecordSnapshot validAutomatic =
                LegacyRecord("rec-auto", "dedup-auto", "evt-shape-winner", "relation.spouse.gained");
            malformedWinner.sourceEventId = "evt-shape-winner";
            var unsafeWinner = new MemoryLegacyOwnerMigrationInput
            {
                ownerPawnId = "Pawn_Shape",
                ruleMap = input.ruleMap,
                records = new List<MemoryLegacyRecordSnapshot>
                    { malformedWinner, validAutomatic }
            };
            AssertTrue("migration.unsafe-winner.owner-raw",
                MemoryThreadMigrationPolicy.PlanDryRun(unsafeWinner).ownerRemainsRaw);
        }

        private static void TestImportedBudget()
        {
            var caps = new
            {
                maxOwnerRows = 10,
                maxGlobalRows = 20,
                ownerBytes = 1000L,
                globalBytes = 2000L,
                combinedCurrent = 0L,
                combinedCap = 3000L
            };

            // Ordering: tick ascending, ageUnknown last, unresolved unit last of all.
            var units = new List<MemoryImportedAdmissionUnit>
            {
                new MemoryImportedAdmissionUnit
                {
                    ownerPawnId = "Pawn_B", sourceIndex = 0,
                    earliestAuthoredTick = 100, rowCount = 5, logicalBytes = 400
                },
                new MemoryImportedAdmissionUnit
                {
                    ownerPawnId = "Pawn_A", sourceIndex = 1,
                    earliestAuthoredTick = 50, anyAgeUnknown = true, rowCount = 3,
                    logicalBytes = 300
                },
                new MemoryImportedAdmissionUnit
                {
                    ownerPawnId = string.Empty, sourceIndex = 2,
                    earliestAuthoredTick = 10, rowCount = 2, logicalBytes = 200
                },
                new MemoryImportedAdmissionUnit
                {
                    // §T17.5: one unit per resolved owner, so this fourth unit uses its own ID.
                    ownerPawnId = "Pawn_D", sourceIndex = 3,
                    earliestAuthoredTick = 60, rowCount = 4, logicalBytes = 350
                }
            };

            MemoryImportedAdmissionDecision decision = ImportedPayloadBudget.PlanAdmission(
                units, caps.maxOwnerRows, caps.maxGlobalRows,
                caps.ownerBytes, caps.globalBytes, caps.combinedCurrent, caps.combinedCap);
            AssertEqual("import.admit.all.outcome",
                nameof(MemoryImportedAdmissionOutcome.Admitted), decision.outcome.ToString());
            AssertEqual("import.admit.all.rows", 14L, decision.totalRows);
            AssertEqual("import.admit.all.bytes", 1250L, decision.totalBytes);
            for (int i = 0; i < units.Count; i++)
            {
                AssertTrue("import.admitted." + i, decision.admitted[i]);
            }

            // Whole-unit rule: a unit over its OWNER cap stays pending while unrelated earlier
            // units remain admitted; no prefix of one owner ever commits (§T13.5).
            var oversizedOwnerUnit = new List<MemoryImportedAdmissionUnit>
            {
                new MemoryImportedAdmissionUnit
                {
                    ownerPawnId = "Pawn_B", sourceIndex = 0,
                    earliestAuthoredTick = 10, rowCount = 4, logicalBytes = 100
                },
                new MemoryImportedAdmissionUnit
                {
                    ownerPawnId = "Pawn_C", sourceIndex = 1,
                    earliestAuthoredTick = 20, rowCount = 99, logicalBytes = 100
                },
                new MemoryImportedAdmissionUnit
                {
                    ownerPawnId = "Pawn_A", sourceIndex = 2,
                    earliestAuthoredTick = 30, rowCount = 2, logicalBytes = 50
                }
            };
            MemoryImportedAdmissionDecision partial = ImportedPayloadBudget.PlanAdmission(
                oversizedOwnerUnit, caps.maxOwnerRows, caps.maxGlobalRows,
                caps.ownerBytes, caps.globalBytes, caps.combinedCurrent, caps.combinedCap);
            AssertEqual("import.partial.outcome", "Pending", partial.outcome.ToString());
            AssertTrue("import.partial.first-admitted", partial.admitted[0]);
            AssertTrue("import.partial.oversized-pending", !partial.admitted[1]);
            AssertTrue("import.partial.later-still-committed", partial.admitted[2]);

            // Global byte cap exhaustion flips later units to pending without invalidating earlier.
            var tightGlobal = new List<MemoryImportedAdmissionUnit>
            {
                new MemoryImportedAdmissionUnit
                {
                    ownerPawnId = "Pawn_A", sourceIndex = 0,
                    earliestAuthoredTick = 10, rowCount = 1, logicalBytes = 900
                },
                new MemoryImportedAdmissionUnit
                {
                    ownerPawnId = "Pawn_B", sourceIndex = 1,
                    earliestAuthoredTick = 20, rowCount = 1, logicalBytes = 500
                }
            };
            MemoryImportedAdmissionDecision exhausted = ImportedPayloadBudget.PlanAdmission(
                tightGlobal, caps.maxOwnerRows, caps.maxGlobalRows,
                1000L, 1200L, caps.combinedCurrent, caps.combinedCap);
            AssertTrue("import.exhaust.first-admitted", exhausted.admitted[0]);
            AssertTrue("import.exhaust.second-pending", !exhausted.admitted[1]);

            // Invalid configuration fails closed: bad caps, duplicate owner units, and more
            // than one unresolved unit each refuse the whole round (§T17.5 whole-unit rules).
            MemoryImportedAdmissionDecision invalid = ImportedPayloadBudget.PlanAdmission(
                units, 0, caps.maxGlobalRows, caps.ownerBytes, caps.globalBytes,
                caps.combinedCurrent, caps.combinedCap);
            AssertEqual("import.invalid-caps", "Invalid", invalid.outcome.ToString());
            var duplicateOwnerUnits = new List<MemoryImportedAdmissionUnit>
            {
                new MemoryImportedAdmissionUnit
                    { ownerPawnId = "Pawn_A", sourceIndex = 0, earliestAuthoredTick = 10, rowCount = 1, logicalBytes = 10 },
                new MemoryImportedAdmissionUnit
                    { ownerPawnId = "Pawn_A", sourceIndex = 1, earliestAuthoredTick = 20, rowCount = 1, logicalBytes = 10 }
            };
            MemoryImportedAdmissionDecision duplicateOwner = ImportedPayloadBudget.PlanAdmission(
                duplicateOwnerUnits, caps.maxOwnerRows, caps.maxGlobalRows,
                caps.ownerBytes, caps.globalBytes, caps.combinedCurrent, caps.combinedCap);
            AssertEqual("import.duplicate-owner-invalid", "Invalid", duplicateOwner.outcome.ToString());
            var twoUnresolvedUnits = new List<MemoryImportedAdmissionUnit>
            {
                new MemoryImportedAdmissionUnit
                    { ownerPawnId = string.Empty, sourceIndex = 0, earliestAuthoredTick = 10, rowCount = 1, logicalBytes = 10 },
                new MemoryImportedAdmissionUnit
                    { ownerPawnId = string.Empty, sourceIndex = 1, earliestAuthoredTick = 20, rowCount = 1, logicalBytes = 10 }
            };
            MemoryImportedAdmissionDecision twoUnresolved = ImportedPayloadBudget.PlanAdmission(
                twoUnresolvedUnits, caps.maxOwnerRows, caps.maxGlobalRows,
                caps.ownerBytes, caps.globalBytes, caps.combinedCurrent, caps.combinedCap);
            AssertEqual("import.two-unresolved-invalid", "Invalid", twoUnresolved.outcome.ToString());

            // Every candidate and committed addition is checked. A combined or cumulative overflow
            // invalidates the WHOLE decision; it may never masquerade as Pending or Admitted.
            var combinedOverflowUnits = new List<MemoryImportedAdmissionUnit>
            {
                new MemoryImportedAdmissionUnit
                {
                    ownerPawnId = "Pawn_Max", sourceIndex = 0,
                    earliestAuthoredTick = 1, rowCount = 1, logicalBytes = long.MaxValue
                }
            };
            MemoryImportedAdmissionDecision combinedOverflow =
                ImportedPayloadBudget.PlanAdmission(
                    combinedOverflowUnits,
                    int.MaxValue,
                    int.MaxValue,
                    long.MaxValue,
                    long.MaxValue,
                    1,
                    long.MaxValue);
            AssertEqual("import.overflow.combined-invalid", "Invalid",
                combinedOverflow.outcome.ToString());

            var cumulativeOverflowUnits = new List<MemoryImportedAdmissionUnit>
            {
                new MemoryImportedAdmissionUnit
                {
                    ownerPawnId = "Pawn_A", sourceIndex = 0,
                    earliestAuthoredTick = 1, rowCount = 1,
                    logicalBytes = (long.MaxValue / 2) + 1
                },
                new MemoryImportedAdmissionUnit
                {
                    ownerPawnId = "Pawn_B", sourceIndex = 1,
                    earliestAuthoredTick = 2, rowCount = 1,
                    logicalBytes = (long.MaxValue / 2) + 1
                }
            };
            MemoryImportedAdmissionDecision cumulativeOverflow =
                ImportedPayloadBudget.PlanAdmission(
                    cumulativeOverflowUnits,
                    int.MaxValue,
                    int.MaxValue,
                    long.MaxValue,
                    long.MaxValue,
                    0,
                    long.MaxValue);
            AssertEqual("import.overflow.cumulative-invalid", "Invalid",
                cumulativeOverflow.outcome.ToString());

            var exactBoundaryUnits = new List<MemoryImportedAdmissionUnit>
            {
                new MemoryImportedAdmissionUnit
                {
                    ownerPawnId = "Pawn_Boundary", sourceIndex = 0,
                    earliestAuthoredTick = 1, rowCount = 1,
                    logicalBytes = long.MaxValue - 1
                }
            };
            MemoryImportedAdmissionDecision exactBoundary =
                ImportedPayloadBudget.PlanAdmission(
                    exactBoundaryUnits,
                    int.MaxValue,
                    int.MaxValue,
                    long.MaxValue,
                    long.MaxValue,
                    1,
                    long.MaxValue);
            AssertEqual("import.overflow.exact-boundary-admitted", "Admitted",
                exactBoundary.outcome.ToString());
            AssertEqual("import.overflow.exact-boundary-bytes", long.MaxValue - 1,
                exactBoundary.totalBytes);
        }

        private static MemoryRootIdentity Root(
            string owner,
            string epoch,
            string subjectKind,
            string subjectId)
        {
            return new MemoryRootIdentity
            {
                ownerPawnId = owner,
                ownerEpochToken = epoch,
                primarySubjectKind = subjectKind,
                primarySubjectId = subjectId
            };
        }

        private static string Epoch(long sequence)
        {
            return OrdinalSegmentCodec.Segment("memory-epoch-v1")
                + OrdinalSegmentCodec.Segment(sequence.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        private static string Faction(string factionInstanceId, long generation)
        {
            string subjectId;
            AssertTrue("helper.faction.create",
                MemoryIdentityCodec.TryCreateFactionSubjectId(
                    factionInstanceId,
                    generation,
                    out subjectId));
            return subjectId;
        }

        private static MemoryTypedSubject Subject(string kind, string id)
        {
            return new MemoryTypedSubject { subjectKind = kind, subjectId = id };
        }

        private static MemoryThreadRouteRule Route(string subjectKind, params string[] extractors)
        {
            MemoryThreadRouteRule route = new MemoryThreadRouteRule { subjectKind = subjectKind };
            route.equivalentExtractors.AddRange(extractors.Select(value =>
                new MemoryRouteExtractor { extractorToken = value }));
            return route;
        }

        private static MemoryRouteCandidate Candidate(
            string extractor,
            string kind,
            string id,
            string label)
        {
            return new MemoryRouteCandidate
            {
                extractorToken = extractor,
                subjectKind = kind,
                subjectId = id,
                frozenLabel = label
            };
        }

        private static MemoryFactDescriptor Fact(string factKind, string aggregation, string valueKind)
        {
            return new MemoryFactDescriptor
            {
                factKind = factKind,
                aggregationToken = aggregation,
                canonicalValueKind = valueKind
            };
        }

        internal static string RepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null
                && !File.Exists(Path.Combine(directory.FullName, "design", "MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md")))
                directory = directory.Parent;
            if (directory == null) throw new InvalidOperationException("Repository root not found.");
            return directory.FullName;
        }

        private static string Text(XElement parent, string name)
        {
            XElement element = parent == null ? null : parent.Element(name);
            return element == null ? string.Empty : element.Value.Trim();
        }

        private static bool Bool(XElement parent, string name)
        {
            bool value;
            return bool.TryParse(Text(parent, name), out value) && value;
        }

        private static ulong[] UnsignedTuple(string encoded)
        {
            return encoded.Split('/').Select(value => ulong.Parse(
                value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }

        private static List<string> ReadSegments(string encoded)
        {
            List<string> values = new List<string>();
            int offset = 0;
            while (offset < encoded.Length)
            {
                string value;
                AssertTrue("helper.segment.read." + offset,
                    OrdinalSegmentCodec.TryReadCanonicalSegment(
                        encoded, ref offset, MemoryIdentityCodec.MaximumCanonicalRepairTupleCharacters,
                        true, out value));
                values.Add(value);
            }
            return values;
        }

        private static void AssertRejectsRoot(string name, MemoryRootIdentity root)
        {
            string ignored;
            AssertTrue(name, !MemoryIdentityCodec.TryCreateRootId(root, out ignored));
            AssertEqual(name + ".no-partial", string.Empty, ignored);
        }

        private static string Escape(string value)
        {
            if (value == null) return "null";
            return value.Replace("\uD800", "high-surrogate").Replace("\uDC00", "low-surrogate");
        }

        private static void AssertTrue(string name, bool condition)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("FAILED: " + name);
        }

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    "FAILED: " + name + " expected [" + expected + "] got [" + actual + "]");
            }
        }

        private static void AssertContains(string name, string haystack, string needle)
        {
            assertions++;
            if (haystack == null || haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    "FAILED: " + name + " missing [" + needle + "] in [" + haystack + "]");
            }
        }
    }
}

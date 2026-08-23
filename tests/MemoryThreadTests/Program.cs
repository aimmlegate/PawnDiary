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
                TestExactRoutePolicy();
                TestFactGrammar();
                TestSettingsAndCapacityContracts();
                TestShippedXmlContractsAndReachability();
                TestM0CatalogShape();

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
                    Root("Pawn_A", Epoch(1), "stream", "Pawn_B"), out changedKind)
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
                Root("Pawn_A", Epoch(1), "stream",
                    new string('s', MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters + 1)));

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
                    Subject("faction", "Faction_2"),
                    Subject("pawn", "Pawn_B")
                }
            };
            string first;
            AssertTrue("sourceFallback.create",
                MemoryIdentityCodec.TryCreateSourceOccurrenceFallback(input, out first));
            AssertEqual(
                "sourceFallback.golden",
                "36:memory-source-occurrence-fallback-v112:death.family6:1234561:26:victim1:27:faction9:Faction_24:pawn6:Pawn_B",
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
            AssertEqual("xml.capture.count", 29, defs.Count);
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
                AssertEqual("catalog.payload.declared-field-count", 399, expectedPaths.Count);
                AssertEqual("catalog.payload.atom-count", 399, atoms.Count);
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
            }
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

        private static string RepoRoot()
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

// PawnDiaryMemoryWordingAfterUseFixtureTests.cs — loaded-runtime contracts for memory reuse.
//
// These tests never queue HTTP work. They exercise the private post-publication scheduling seam
// against an inert component shell, and inspect compiled call sites to keep both automatic page
// paths wired to the owner-local cooldown clock and the background wording coordinator.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary;
using RimTestRedux;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Guards the runtime hand-off from a successfully published memory-bearing page to one
    /// optional, background wording refresh without changing canonical memory facts or old prose.
    /// </summary>
    public static class PawnDiaryMemoryWordingAfterUseFixtureTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const string StandaloneWordingRoot = "memory-wording-standalone-v1";
        private const string WinningVariant = "variant-after-use-winning";
        private const string LosingVariant = "variant-after-use-losing";

        /// <summary>
        /// The completed-page clock advances across its whole valid domain and fails closed at
        /// missing, negative, and saturated values instead of wrapping the reuse cooldown open.
        /// </summary>
        [Test]
        public static void CompletedEntryOrdinalAdvanceIsPositiveAndSaturating()
        {
            long next;
            Require(MemoryRepetitionGuardPolicy.TryAdvanceCompletedDiaryEntryOrdinal(1, out next)
                    && next == 2,
                "The first completed automatic page did not advance the owner-local clock.");
            Require(MemoryRepetitionGuardPolicy.TryAdvanceCompletedDiaryEntryOrdinal(
                        long.MaxValue - 1, out next)
                    && next == long.MaxValue,
                "The last representable completed-page advance was refused.");
            Require(!MemoryRepetitionGuardPolicy.TryAdvanceCompletedDiaryEntryOrdinal(
                        long.MaxValue, out next)
                    && next == long.MaxValue,
                "A saturated completed-page clock wrapped or changed.");
            Require(!MemoryRepetitionGuardPolicy.TryAdvanceCompletedDiaryEntryOrdinal(0, out next)
                    && next == 0,
                "A missing completed-page clock was treated as valid.");
            Require(!MemoryRepetitionGuardPolicy.TryAdvanceCompletedDiaryEntryOrdinal(-1, out next)
                    && next == -1,
                "A malformed negative completed-page clock was treated as valid.");
        }

        /// <summary>
        /// Every automatic publication adapter must advance the cooldown clock before post-use work.
        /// This checks the compiled assembly, so a refactor cannot silently leave ordinary pages,
        /// generated reflections, or direct integration pages out of the shared lifecycle.
        /// </summary>
        [Test]
        public static void MainAndReflectionPublicationWireOrdinalThenBackgroundRewrite()
        {
            MethodInfo main = RequirePrivateMethod("ApplyLlmResult");
            MethodInfo reflection = RequirePrivateMethod("ApplyMemoryReflectionCoordinatorResult");
            MethodInfo direct = typeof(DiaryGameComponent).GetMethod(
                "ApplyExternalDirectEntryText",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo advance = RequirePrivateMethod("AdvanceCompletedAutomaticPageOrdinal");
            MethodInfo schedule = RequirePrivateMethod("ScheduleUsedMemoryWording");
            Require(direct != null, "The direct-entry publication adapter was renamed.");

            int mainAdvance = DirectCallOffset(main, advance);
            int mainSchedule = DirectCallOffset(main, schedule);
            Require(mainAdvance >= 0 && mainSchedule > mainAdvance,
                "The ordinary LLM result path no longer advances the completed-page clock before "
                + "offering the background memory rewrite.");

            int directAdvance = DirectCallOffset(direct, advance);
            Require(directAdvance >= 0,
                "The direct-entry publication adapter no longer advances the completed-page clock.");

            int reflectionPublish = DirectCallOffset(reflection, direct);
            int reflectionSchedule = DirectCallOffset(reflection, schedule);
            Require(reflectionPublish >= 0 && reflectionSchedule > reflectionPublish,
                "The generated memory-reflection path no longer commits through the counted direct "
                + "publication adapter before offering the background rewrite.");
        }

        /// <summary>
        /// A successful page schedules exactly the single evidence row carried by its winning
        /// prompt variant. The current optional prose remains readable until a later coordinator
        /// result successfully replaces it.
        /// </summary>
        [Test]
        public static void PostUseSchedulingChoosesWinningSingleEvidenceAndKeepsOldWording()
        {
            WithOptionalMemoryPolicy(() =>
            {
                PawnKnowledgeState owner = NewOwner("Pawn_AfterUse_Winner");
                SavedMemoryBlock winner = NewBlock(
                    owner, "record-after-use-winner", "source-after-use-winner",
                    "I still remember the old promise.", 4);
                SavedMemoryBlock loser = NewBlock(
                    owner, "record-after-use-loser", "source-after-use-loser",
                    "Another memory belongs only to the losing prompt variant.", 2);
                owner.standaloneBlocks.Add(winner);
                owner.standaloneBlocks.Add(loser);
                DiaryGameComponent component = NewComponent(owner);
                SavedActiveLogicalRequestV1 request = NewRequest(owner);
                request.frozenVariants.Add(VariantFor(LosingVariant, loser));
                request.frozenVariants.Add(VariantFor(WinningVariant, winner));

                string oldText = winner.optionalLlmWording;
                string oldFingerprint = winner.optionalLlmFingerprint;
                long oldRevision = winner.optionalLlmWordingRevision;
                long oldFormatRevision = winner.optionalLlmFormatRevision;
                int oldCategoryMask = winner.optionalLlmCategoryMask;

                InvokePostUseScheduler(
                    component,
                    request,
                    new MemoryInvocationCommitPermitV1 { variantKey = WinningVariant },
                    60000L);

                List<SavedSummaryWordingOpportunityV1> slot = WordingSlot(component);
                SavedSummaryWordingOpportunityV1 pending = slot.Count == 1 ? slot[0] : null;
                SummaryWordingOpportunitySnapshot parsed;
                Require(pending != null
                        && pending.ownerPawnId == owner.pawnId
                        && pending.ownerEpochToken == owner.autobiographicalEpochToken
                        && pending.rootId == StandaloneWordingRoot
                        && pending.summaryRecordId == winner.recordId
                        && pending.expectedOptionalLlmWordingRevision == oldRevision
                        && pending.requestedTick == 60000L
                        && pending.dueTick == 60000L
                        && MemoryOptionalAiPolicy.TryParseSummaryOpportunityKey(
                            pending.opportunityKey, out parsed)
                        && parsed.summaryRecordId == winner.recordId,
                    "The post-use hook did not publish one exact opportunity for the winning "
                    + "variant's single evidence row.");
                Require(winner.optionalLlmWording == oldText
                        && winner.optionalLlmFingerprint == oldFingerprint
                        && winner.optionalLlmWordingRevision == oldRevision
                        && winner.optionalLlmFormatRevision == oldFormatRevision
                        && winner.optionalLlmCategoryMask == oldCategoryMask,
                    "Scheduling erased or mutated the current prose before a successful replacement.");
                Require(loser.optionalLlmWordingRevision == 2
                        && loser.optionalLlmWording.Contains("losing prompt variant"),
                    "The losing prompt variant's memory was mutated or selected.");
            });
        }

        /// <summary>
        /// Empty and multi-memory winning receipts fail closed: neither can guess which memory to
        /// rewrite, and neither consumes the owner's single saved wording slot.
        /// </summary>
        [Test]
        public static void ZeroOrMultipleWinningEvidenceDoesNotSchedule()
        {
            WithOptionalMemoryPolicy(() =>
            {
                PawnKnowledgeState owner = NewOwner("Pawn_AfterUse_Cardinality");
                SavedMemoryBlock first = NewBlock(
                    owner, "record-after-use-first", "source-after-use-first",
                    "The first existing wording.", 1);
                SavedMemoryBlock second = NewBlock(
                    owner, "record-after-use-second", "source-after-use-second",
                    "The second existing wording.", 1);
                owner.standaloneBlocks.Add(first);
                owner.standaloneBlocks.Add(second);
                DiaryGameComponent component = NewComponent(owner);

                SavedActiveLogicalRequestV1 empty = NewRequest(owner);
                empty.frozenVariants.Add(new SavedFrozenPromptVariantV1
                {
                    variantKey = WinningVariant,
                    receiptPlan = new SavedFrozenEvidenceReceiptPlanV1()
                });
                InvokePostUseScheduler(
                    component,
                    empty,
                    new MemoryInvocationCommitPermitV1 { variantKey = WinningVariant },
                    61000L);
                Require(WordingSlot(component).Count == 0,
                    "A receipt with zero evidence scheduled a guessed wording refresh.");

                SavedActiveLogicalRequestV1 multiple = NewRequest(owner);
                SavedFrozenPromptVariantV1 two = VariantFor(WinningVariant, first);
                two.receiptPlan.evidenceEntries.Add(EvidenceFor(second));
                multiple.frozenVariants.Add(two);
                InvokePostUseScheduler(
                    component,
                    multiple,
                    new MemoryInvocationCommitPermitV1 { variantKey = WinningVariant },
                    62000L);
                Require(WordingSlot(component).Count == 0,
                    "A receipt with two evidence rows scheduled an ambiguous wording refresh.");
                Require(first.optionalLlmWordingRevision == 1
                        && second.optionalLlmWordingRevision == 1,
                    "A rejected cardinality path mutated an existing wording revision.");
            });
        }

        /// <summary>
        /// A failed generated reflection returns before publication, so it advances neither the
        /// completed-page ordinal nor the optional wording slot.
        /// </summary>
        [Test]
        public static void FailedReflectionPageDoesNotAdvanceOrSchedule()
        {
            PawnKnowledgeState owner = NewOwner("Pawn_AfterUse_Failed");
            SavedMemoryBlock block = NewBlock(
                owner, "record-after-use-failed", "source-after-use-failed",
                "This wording must survive a failed page.", 3);
            owner.standaloneBlocks.Add(block);
            DiaryGameComponent component = NewComponent(owner);
            SavedActiveLogicalRequestV1 request = NewRequest(owner);
            request.requestPurposeToken = MemoryDispatchTokens.MemoryReflection;
            request.frozenVariants.Add(VariantFor(WinningVariant, block));
            long ordinalBefore = owner.completedDiaryEntryOrdinal;

            MethodInfo apply = RequirePrivateMethod("ApplyMemoryReflectionCoordinatorResult");
            apply.Invoke(component, new object[]
            {
                request,
                new LlmGenerationResult
                {
                    success = false,
                    generatedText = string.Empty,
                    memoryInvocationPermit =
                        new MemoryInvocationCommitPermitV1 { variantKey = WinningVariant }
                }
            });

            Require(owner.completedDiaryEntryOrdinal == ordinalBefore,
                "A failed generated reflection advanced the completed-page cooldown clock.");
            Require(WordingSlot(component).Count == 0,
                "A failed generated reflection scheduled a memory wording refresh.");
            Require(block.optionalLlmWordingRevision == 3
                    && block.optionalLlmWording.Contains("survive a failed page"),
                "A failed generated reflection changed the current memory prose.");
        }

        private static PawnKnowledgeState NewOwner(string ownerPawnId)
        {
            PawnKnowledgeState owner = PawnKnowledgeState.CreateCurrent(ownerPawnId);
            owner.autobiographicalEpochToken = EpochToken(91);
            return owner;
        }

        private static SavedMemoryBlock NewBlock(
            PawnKnowledgeState owner,
            string recordId,
            string sourceOccurrenceId,
            string oldWording,
            long wordingRevision)
        {
            const long formatRevision = 1;
            string canonical = "A lasting canonical fact for " + recordId + ".";
            string fingerprint;
            Require(MemoryOptionalAiPolicy.TryCreateBlockWordingProjectionFingerprint(
                    recordId,
                    MemoryContractTokens.KindEvent,
                    MemoryContractTokens.CategoryPersonal,
                    canonical,
                    formatRevision,
                    out fingerprint),
                "The fixture could not fingerprint its canonical memory wording.");
            return new SavedMemoryBlock
            {
                recordId = recordId,
                sourceOccurrenceId = sourceOccurrenceId,
                ownerPawnId = owner.pawnId,
                ownerEpochToken = owner.autobiographicalEpochToken,
                kind = MemoryContractTokens.KindEvent,
                category = MemoryContractTokens.CategoryPersonal,
                importance = MemoryContractTokens.ImportanceRegular,
                automaticWording = canonical,
                optionalLlmWording = oldWording,
                optionalLlmWordingRevision = wordingRevision,
                optionalLlmFingerprint = fingerprint,
                optionalLlmFormatRevision = formatRevision,
                optionalLlmCategoryMask = MemoryCategoryBits.Personal,
                formatRevision = 1,
                providerExposureState = "confirmed_sent"
            };
        }

        private static DiaryGameComponent NewComponent(PawnKnowledgeState owner)
        {
            var record = new PawnDiaryRecord
            {
                pawnId = owner.pawnId,
                knowledgeState = owner
            };
            DiaryGameComponent component = PawnDiaryMemoryM1FixtureTests.NewMemoryComponent(
                new List<PawnDiaryRecord> { record },
                new List<SavedActiveLogicalRequestV1>(),
                new List<SavedImportedMemoryRow>());
            SetPrivateField(component, "diariesById",
                new Dictionary<string, PawnDiaryRecord>(StringComparer.Ordinal)
                {
                    { owner.pawnId, record }
                });
            SetPrivateField(component, "globalOptionalRequestCancellationGeneration", 7L);
            return component;
        }

        private static SavedActiveLogicalRequestV1 NewRequest(PawnKnowledgeState owner)
        {
            return new SavedActiveLogicalRequestV1
            {
                requestPurposeToken = MemoryDispatchTokens.NormalDiary,
                ownerPawnId = owner.pawnId,
                ownerEpochToken = owner.autobiographicalEpochToken
            };
        }

        private static SavedFrozenPromptVariantV1 VariantFor(
            string variantKey,
            SavedMemoryBlock block)
        {
            var receipt = new SavedFrozenEvidenceReceiptPlanV1();
            receipt.evidenceEntries.Add(EvidenceFor(block));
            return new SavedFrozenPromptVariantV1
            {
                variantKey = variantKey,
                receiptPlan = receipt
            };
        }

        private static SavedFrozenEvidenceEntryV1 EvidenceFor(SavedMemoryBlock block)
        {
            return new SavedFrozenEvidenceEntryV1
            {
                recordId = block.recordId,
                sourceOccurrenceId = block.sourceOccurrenceId,
                rootIdOrEmpty = block.rootId ?? string.Empty
            };
        }

        private static void InvokePostUseScheduler(
            DiaryGameComponent component,
            SavedActiveLogicalRequestV1 request,
            MemoryInvocationCommitPermitV1 permit,
            long nowTick)
        {
            RequirePrivateMethod("ScheduleUsedMemoryWording").Invoke(
                component, new object[] { request, permit, nowTick });
        }

        private static List<SavedSummaryWordingOpportunityV1> WordingSlot(
            DiaryGameComponent component)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(
                "summaryWordingOpportunities", PrivateInstance);
            Require(field != null, "The saved wording-slot fixture seam was renamed.");
            var value = field.GetValue(component) as List<SavedSummaryWordingOpportunityV1>;
            Require(value != null, "The saved wording slot was not initialized.");
            return value;
        }

        private static void WithOptionalMemoryPolicy(Action body)
        {
            MemoryPolicySnapshot prior = MemoryEffectivePolicyProvider.Current;
            Require(prior != null, "No published memory policy was available to the fixture.");
            MemorySettingsPolicyFieldsV1 fields = prior.ToFields();
            fields.saveNewMemories = true;
            fields.useMemoriesInWriting = true;
            fields.allowExtraMemoryAiRequests = true;
            fields.memoryCategoryMask |= MemoryCategoryBits.Personal;
            fields.optionalRequestInvalidationGeneration = Math.Max(
                1L, fields.optionalRequestInvalidationGeneration);
            MemoryPolicySnapshot enabled = MemoryPolicyNormalizer.Normalize(
                MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
                fields,
                MemoryPolicyDefAdapter.Bounds());
            Require(enabled.AllowsOptionalRequests
                    && MemoryEffectivePolicyProvider.Publish(enabled),
                "The fixture could not publish optional memory policy.");
            try
            {
                body();
            }
            finally
            {
                MemoryEffectivePolicyProvider.Publish(prior);
            }
        }

        private static MethodInfo RequirePrivateMethod(string name)
        {
            MethodInfo method = typeof(DiaryGameComponent).GetMethod(name, PrivateInstance);
            Require(method != null, "The private runtime fixture seam '" + name + "' was renamed.");
            return method;
        }

        private static int DirectCallOffset(MethodInfo caller, MethodInfo callee)
        {
            byte[] body = caller?.GetMethodBody()?.GetILAsByteArray();
            if (body == null || callee == null) return -1;
            byte[] token = BitConverter.GetBytes(callee.MetadataToken);
            for (int index = 0; index + token.Length < body.Length; index++)
            {
                // ECMA-335 call and callvirt both carry a four-byte metadata token immediately after
                // their one-byte opcode. Both target methods are private members of this same type,
                // so their MethodDef token is emitted directly rather than through a MemberRef.
                if (body[index] != 0x28 && body[index] != 0x6F) continue;
                bool match = true;
                for (int byteIndex = 0; byteIndex < token.Length; byteIndex++)
                {
                    if (body[index + 1 + byteIndex] == token[byteIndex]) continue;
                    match = false;
                    break;
                }
                if (match) return index;
            }
            return -1;
        }

        private static void SetPrivateField(
            DiaryGameComponent component,
            string name,
            object value)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(name, PrivateInstance);
            Require(field != null, "The private component field '" + name + "' was renamed.");
            field.SetValue(component, value);
        }

        private static string EpochToken(long sequence)
        {
            return OrdinalSegmentCodec.Segment("memory-epoch-v1")
                + OrdinalSegmentCodec.Segment(sequence.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }
    }
}

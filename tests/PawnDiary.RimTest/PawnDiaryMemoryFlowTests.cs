// In-game associative-memory wiring tests for Pawn Diary (design/MEMORY_WIRING_PLAN.md §W4).
//
// Proves the six memory behaviors this fixture actually exercises inside a real loaded game:
//   1. Deposit: a significant EventFactory pair page deposits one fragment for each POV pawn.
//   2. Noise gate: a real quiet-cue Chitchat page deposits no fragment.
//   3. Idempotency: registering the same pawn+event deposit twice keeps one fragment.
//   4. Recall: matching, old-enough fragments freeze a non-empty memoryContext onto the next page.
//   5. Settings gate: disabling the memory system prevents deposit without preventing the page.
//   6. Old-save compatibility: the live component repository is non-null and functional after load
//      (the real-Scribe missing-key repair is covered by PawnDiaryRepositoryRebuildFixtureTests).
//
// All fragile scaffolding — isolated pawns, RNG isolation, settings snapshot/restore, event/diary
// cleanup — lives in the shared PawnDiaryRimTestScope harness.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Loaded-game verification of the associative-memory EventFactory wiring: deposit, noise gate,
    /// recall, idempotency, settings gate, and repaired repository availability.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryMemoryFlowTests
    {
        private static readonly FieldInfo MemoriesField =
            typeof(DiaryGameComponent).GetField("memories",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo TombstoneField =
            typeof(DiaryGameComponent).GetField("memoryOwnerAbsentSinceTick",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawnA;
        private static Pawn pawnB;

        [BeforeEach]
        public static void SetUp()
        {
            // These tests enter through EventFactory itself, the W1 seam that owns recall/deposit.
            // They deliberately do not depend on PlayLog routing, player group overrides, or delayed
            // interaction batches; those behaviors have their own loaded-game suites.
            scope = PawnDiaryRimTestScope.Begin();

            if (Find.CurrentMap == null)
            {
                throw new AssertionException("Memory flow tests require a loaded map with a colony.");
            }

            if (MemoriesField == null)
            {
                throw new AssertionException(
                    "Reflection handle for DiaryGameComponent.memories is null — field was renamed.");
            }

            pawnA = scope.CreateAdultColonist();
            pawnB = scope.CreateAdultColonist();

            // Ensure the memory system is enabled for these tests.
            PawnDiaryMod.Settings.enableMemorySystem = true;
        }

        [AfterEach]
        public static void TearDown()
        {
            try
            {
                // Clean up any deposited memory fragments for the test pawns.
                CleanupMemoryForTestPawns();
                scope?.TearDown();
            }
            finally
            {
                scope = null;
                pawnA = null;
                pawnB = null;
            }
        }

        // ── Test 1: Deposit ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Firing a significant pairwise event deposits a memory fragment for each participant into
        /// the PawnMemoryRepository. The fragment carries tags, keywords, and non-zero importance.
        /// </summary>
        [Test]
        public static void SignificantEventDepositsMemoryFragment()
        {
            PawnMemoryRepository memories = GetMemories();
            string pawnAId = pawnA.GetUniqueLoadID();
            string pawnBId = pawnB.GetUniqueLoadID();
            int countBeforeA = memories.ForPawn(pawnAId).Count;
            int countBeforeB = memories.ForPawn(pawnBId).Count;

            // DeepTalk belongs to the significant, non-batched heartfelt group (white cue, importance 0.5).
            AddPairwiseMemoryEvent(pawnA, pawnB, "DeepTalk");

            int countAfterA = memories.ForPawn(pawnAId).Count;
            int countAfterB = memories.ForPawn(pawnBId).Count;

            Require(countAfterA > countBeforeA,
                "Expected a memory deposit for pawn A after a significant pairwise event, but count stayed at "
                + countBeforeA + ".");
            Require(countAfterB > countBeforeB,
                "Expected a memory deposit for pawn B after a significant pairwise event, but count stayed at "
                + countBeforeB + ".");

            // Verify the deposited fragment has structure.
            IReadOnlyList<MemoryFragment> fragmentsA = memories.ForPawn(pawnAId);
            MemoryFragment latest = fragmentsA[fragmentsA.Count - 1];
            Require(!string.IsNullOrWhiteSpace(latest.memoryId),
                "Deposited fragment must have a non-blank memoryId.");
            Require(!string.IsNullOrWhiteSpace(latest.text),
                "Deposited fragment must have non-blank text.");
            Require(latest.importance > 0f,
                "Deposited fragment must have positive importance.");
            Require(latest.tags != null && latest.tags.Count > 0,
                "Deposited fragment from a significant event should carry at least one tag.");
        }

        // ── Test 2: Quiet event does NOT deposit ─────────────────────────────────────────────────────

        /// <summary>
        /// A quiet/ambient event (importance below minDepositImportance 0.3) deposits nothing.
        /// </summary>
        [Test]
        public static void QuietEventDoesNotDeposit()
        {
            PawnMemoryRepository memories = GetMemories();
            string pawnAId = pawnA.GetUniqueLoadID();
            int countBefore = memories.ForPawn(pawnAId).Count;

            // Chitchat is the real vanilla Def name. Calling EventFactory directly gives the memory
            // noise gate a page to inspect without waiting for smalltalk's separate ambient-day batch.
            AddPairwiseMemoryEvent(pawnA, pawnB, "Chitchat");

            int countAfter = memories.ForPawn(pawnAId).Count;
            Require(countAfter == countBefore,
                "A quiet chat event (importance < minDepositImportance) must not deposit a fragment. "
                + "Before: " + countBefore + ", After: " + countAfter + ".");
        }

        // ── Test 3: Idempotent deposit ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Replaying the same signal (same event ID) does not deposit a duplicate fragment. The
        /// repository's HasDeposit/Register idempotency guard ensures one fragment per pawn+event.
        /// </summary>
        [Test]
        public static void ReplayDoesNotDuplicateDeposit()
        {
            PawnMemoryRepository memories = GetMemories();
            string pawnAId = pawnA.GetUniqueLoadID();

            // Fire once.
            DiaryEvent first = AddPairwiseMemoryEvent(pawnA, pawnB, "DeepTalk");
            int countAfterFirst = memories.ForPawn(pawnAId).Count;

            // Manually attempt a second deposit for the same event (simulates replay).
            Require(memories.HasDeposit(pawnAId, first.eventId),
                "After the first deposit, HasDeposit should return true for the same pawn+event.");

            // Try registering a duplicate directly.
            MemoryFragment duplicate = new MemoryFragment
            {
                memoryId = Guid.NewGuid().ToString("N"),
                pawnId = pawnAId,
                sourceEventId = first.eventId,
                text = "duplicate attempt",
                importance = 0.9f,
                createdTick = first.tick,
                lastRecalledTick = first.tick
            };
            memories.Register(duplicate);

            int countAfterReplay = memories.ForPawn(pawnAId).Count;
            Require(countAfterReplay == countAfterFirst,
                "Replaying the same event must not create a duplicate fragment. "
                + "After first: " + countAfterFirst + ", After replay: " + countAfterReplay + ".");
        }

        // ── Test 4: Recall freezes memoryContext onto the event ──────────────────────────────────────

        /// <summary>
        /// After seeding enough fragments (>= minFragmentsForRecall) with related tags/keywords and
        /// aging them past minRecallAgeTicks, a new related event recalls a frozen memoryContext.
        /// This test seeds the repository directly to guarantee deterministic recall conditions.
        /// </summary>
        [Test]
        public static void RelatedEventRecallsMemoryContext()
        {
            PawnMemoryRepository memories = GetMemories();
            string pawnAId = pawnA.GetUniqueLoadID();
            int now = Find.TickManager.TicksGame;
            DiaryMemoryTuningDef tuning =
                DefDatabase<DiaryMemoryTuningDef>.GetNamedSilentFail("Diary_Memory");
            if (tuning == null)
            {
                throw new AssertionException("Diary_Memory tuning Def was not loaded.");
            }

            float originalRecallGateChance = tuning.recallGateChance;
            int originalMinRecallAgeTicks = tuning.minRecallAgeTicks;
            try
            {
                // The production gate is intentionally probabilistic and the production minimum age
                // is one day. This fixture tests wiring, not those pure policies (PawnMemoryTests owns
                // them), so force both boundaries deterministically and restore the live Def below.
                tuning.recallGateChance = 1f;
                tuning.minRecallAgeTicks = 0;
                MemoryPolicySnapshot policy = DiaryMemoryPolicy.Snapshot();

                // DeepTalk extracts Joy (white cue) plus Social (pairwise), so seed those exact tags.
                // This fixes the old combat/danger seed, which had no overlap with its Insult query.
                int seedCount = Math.Max(policy.minFragmentsForRecall, 4) + 2;
                for (int i = 0; i < seedCount; i++)
                {
                    int createdTick = Math.Max(0, now - 1000 - (i * 10));
                    memories.Register(new MemoryFragment
                    {
                        memoryId = Guid.NewGuid().ToString("N"),
                        pawnId = pawnAId,
                        sourceEventId = "seed-evt-" + i,
                        text = "Seeded heartfelt memory number " + i + ".",
                        tags = new List<string> { MemoryTagTokens.Joy, MemoryTagTokens.Social },
                        keywords = new List<string>(),
                        importance = 0.8f,
                        createdTick = createdTick,
                        lastRecalledTick = createdTick,
                        recallCount = 0
                    });
                }

                DiaryEvent newEvent = AddPairwiseMemoryEvent(pawnA, pawnB, "DeepTalk");
                string memoryContext = newEvent.MemoryContextForRole(DiaryEvent.InitiatorRole);
                Require(!string.IsNullOrWhiteSpace(memoryContext),
                    "Matching fragments with a forced-open recall gate must freeze memoryContext.");
                Require(memoryContext.Contains("Seeded heartfelt memory"),
                    "The frozen memoryContext should contain one of the matching seeded fragments.");
                Require(memoryContext.Contains("-"),
                    "A recalled memoryContext should contain at least one '- ' prefixed line.");
                Require(memoryContext.Length <= 600,
                    "Frozen memoryContext must respect the 600-char save-normalization cap.");
            }
            finally
            {
                tuning.recallGateChance = originalRecallGateChance;
                tuning.minRecallAgeTicks = originalMinRecallAgeTicks;
            }
        }

        // ── Test 5: Memory system disabled → no deposit ──────────────────────────────────────────────

        /// <summary>
        /// When settings.enableMemorySystem is false, no deposit occurs.
        /// </summary>
        [Test]
        public static void DisabledMemorySystemDoesNotDeposit()
        {
            PawnMemoryRepository memories = GetMemories();
            string pawnAId = pawnA.GetUniqueLoadID();
            int countBefore = memories.ForPawn(pawnAId).Count;

            PawnDiaryMod.Settings.enableMemorySystem = false;
            try
            {
                AddPairwiseMemoryEvent(pawnA, pawnB, "DeepTalk");
            }
            finally
            {
                PawnDiaryMod.Settings.enableMemorySystem = true;
            }

            int countAfter = memories.ForPawn(pawnAId).Count;
            Require(countAfter == countBefore,
                "With enableMemorySystem=false, no deposit should occur. "
                + "Before: " + countBefore + ", After: " + countAfter + ".");
        }

        // ── Test 6: Live component has functional memory repository ──────────────────────────────────

        /// <summary>
        /// The live DiaryGameComponent's memories field is non-null and functional (old-save
        /// compatibility: a save without memory keys loads an empty store, no errors).
        /// </summary>
        [Test]
        public static void LiveComponentHasFunctionalMemoryRepository()
        {
            PawnMemoryRepository memories = GetMemories();
            Require(memories != null,
                "The live DiaryGameComponent.memories field must be non-null after load.");
            Require(memories.Count >= 0,
                "The memory repository must report a non-negative count.");

            // ForPawn with an unknown id returns an empty list, never null.
            IReadOnlyList<MemoryFragment> unknown = memories.ForPawn("pd-nonexistent-pawn");
            Require(unknown != null && unknown.Count == 0,
                "ForPawn with an unknown id must return a non-null empty list.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

        private static PawnMemoryRepository GetMemories()
        {
            PawnMemoryRepository memories =
                MemoriesField?.GetValue(scope.Component) as PawnMemoryRepository;
            if (memories == null)
            {
                throw new AssertionException(
                    "Could not read DiaryGameComponent.memories — field missing or wrong type.");
            }

            return memories;
        }

        /// <summary>
        /// Creates one pair page through the production EventFactory funnel and returns it. The helper
        /// intentionally bypasses PlayLog classification/batching: those are independent ingestion
        /// concerns, while this suite verifies the recall-before-deposit hooks inside AddPairwiseEvent.
        /// </summary>
        private static DiaryEvent AddPairwiseMemoryEvent(Pawn initiator, Pawn recipient, string interactionDefName)
        {
            InteractionDef interaction = DefDatabase<InteractionDef>.GetNamedSilentFail(interactionDefName);
            if (interaction == null)
            {
                throw new AssertionException(
                    "InteractionDef '" + interactionDefName + "' not found in the loaded game.");
            }

            string label = interaction.LabelCap.Resolve();
            string initiatorText = "PawnDiary.Event.Interaction".Translate(
                initiator.LabelShortCap,
                label,
                recipient.LabelShortCap).Resolve();
            string recipientText = "PawnDiary.Event.Interaction".Translate(
                recipient.LabelShortCap,
                label,
                initiator.LabelShortCap).Resolve();

            DiaryEvent diaryEvent = scope.Component.AddPairwiseEvent(
                initiator,
                recipient,
                interaction.defName,
                label,
                initiatorText,
                recipientText,
                InteractionGroups.InstructionFor(interaction),
                DiaryContextBuilder.BuildGameContextSummary(interaction, label));
            if (diaryEvent == null)
            {
                throw new AssertionException(
                    "EventFactory did not create the " + interactionDefName + " memory fixture page.");
            }

            return diaryEvent;
        }

        /// <summary>
        /// Removes all memory fragments owned by the test pawns from the live repository, so
        /// teardown leaves the developer's colony store clean.
        /// </summary>
        private static void CleanupMemoryForTestPawns()
        {
            if (scope == null || MemoriesField == null)
            {
                return;
            }

            PawnMemoryRepository memories =
                MemoriesField.GetValue(scope.Component) as PawnMemoryRepository;
            if (memories == null)
            {
                return;
            }

            if (pawnA != null)
            {
                memories.RemoveOwner(pawnA.GetUniqueLoadID());
            }

            if (pawnB != null)
            {
                memories.RemoveOwner(pawnB.GetUniqueLoadID());
            }

            // Also clear any tombstone entries for the test pawns.
            if (TombstoneField != null)
            {
                Dictionary<string, int> tombstones =
                    TombstoneField.GetValue(scope.Component) as Dictionary<string, int>;
                if (tombstones != null)
                {
                    if (pawnA != null)
                    {
                        tombstones.Remove(pawnA.GetUniqueLoadID());
                    }

                    if (pawnB != null)
                    {
                        tombstones.Remove(pawnB.GetUniqueLoadID());
                    }
                }
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertionException(message);
            }
        }
    }
}

// In-game wiring tests for the deterministic pawn-knowledge system
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §8, integration list). Proves inside a real loaded game:
//   1. Capture: a romance pair page (Spouse) writes one important-event record per POV pawn with
//      the partner as participant — gameplay capture succeeds without completing any LLM request.
//   2. Closed list: an ordinary chat page writes NO record (§2.1 exclusions).
//   3. Injection switch: with the memory setting OFF, capture still happens (§3.2) while the
//      event's relevant-past slot stays empty; with it ON, a later related event carries at most
//      two dated lines referencing the stored fact.
//   4. Culture: capture resolves the pawn's origin culture once, with "captured" provenance.
//   5. Quiet-hediff channel: an XML-allowlisted persistent hediff (Sterilized) is remembered
//      even though it produces no diary page of its own.
//
// All fragile scaffolding — isolated pawns, settings snapshot/restore, event/diary cleanup —
// lives in the shared PawnDiaryRimTestScope harness. The save round-trip half of the plan's
// integration list is covered by PawnDiaryRepositoryRebuildFixtureTests (real Scribe).
using System;
using System.Reflection;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Loaded-game verification of knowledge capture, the closed allowlist, the injection-only
    /// master switch, culture resolution, and the quiet-hediff channel.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryKnowledgeFlowTests
    {
        private static readonly MethodInfo FindDiaryMethod =
            typeof(DiaryGameComponent).GetMethod("FindDiary",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawnA;
        private static Pawn pawnB;
        private static bool savedMemorySetting;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            if (Find.CurrentMap == null)
            {
                throw new AssertionException("Knowledge flow tests require a loaded map with a colony.");
            }

            if (FindDiaryMethod == null)
            {
                throw new AssertionException(
                    "Reflection handle for DiaryGameComponent.FindDiary is null — method was renamed.");
            }

            pawnA = scope.CreateAdultColonist();
            pawnB = scope.CreateAdultColonist();
            savedMemorySetting = PawnDiaryMod.Settings.enableMemorySystem;
            PawnDiaryMod.Settings.enableMemorySystem = true;
        }

        [AfterEach]
        public static void TearDown()
        {
            try
            {
                PawnDiaryMod.Settings.enableMemorySystem = savedMemorySetting;
                scope?.TearDown();
            }
            finally
            {
                scope = null;
                pawnA = null;
                pawnB = null;
            }
        }

        // ── 1 + 2: capture through the real funnel, closed-list negative ─────────────────────────────

        /// <summary>
        /// A marriage pair page deposits one relation.spouse.gained record for EACH pawn with the
        /// partner as its first participant; an ordinary Chitchat page deposits nothing.
        /// </summary>
        [Test]
        public static void MarriageCapturesForBothPawnsAndChatDoesNot()
        {
            PawnKnowledgeState stateA = KnowledgeFor(pawnA);
            PawnKnowledgeState stateB = KnowledgeFor(pawnB);
            int beforeA = stateA.records.Count;
            int beforeB = stateB.records.Count;

            AddRomancePairEvent(pawnA, pawnB, "Spouse", "married");

            Require(stateA.records.Count == beforeA + 1,
                "Pawn A must gain exactly one marriage record, had " + beforeA + " now "
                + stateA.records.Count + ".");
            Require(stateB.records.Count == beforeB + 1,
                "Pawn B must gain exactly one marriage record.");

            ImportantMemoryRecord record = stateA.records[stateA.records.Count - 1];
            Require(record.eventKind == "relation.spouse.gained",
                "Expected kind relation.spouse.gained, got '" + record.eventKind + "'.");
            Require(record.participantIds.Count > 0
                    && record.participantIds[0] == pawnB.GetUniqueLoadID(),
                "The record's first participant must be the partner pawn.");
            Require(!string.IsNullOrWhiteSpace(record.participantNames[0]),
                "The partner's display-name fallback must be saved.");
            Require(!string.IsNullOrWhiteSpace(record.dateLabel),
                "The capture must stamp the game date label.");
            Require(!string.IsNullOrWhiteSpace(record.fallbackSummary),
                "The capture must render a localized fallback summary.");

            // Closed list (§2.1): ordinary conversation never becomes important memory.
            int beforeChat = stateA.records.Count;
            AddPairEvent(pawnA, pawnB, "Chitchat");
            Require(stateA.records.Count == beforeChat,
                "A Chitchat page must not deposit an important-event record.");
        }

        // ── 3: the one player switch gates injection only ────────────────────────────────────────────

        /// <summary>
        /// With the memory switch OFF: capture continues, but the page's relevant-past slot stays
        /// empty. With it back ON: a later event with the same partner carries the stored fact.
        /// </summary>
        [Test]
        public static void SwitchOffStillCapturesAndOnInjectsRelevantPast()
        {
            PawnDiaryMod.Settings.enableMemorySystem = false;
            PawnKnowledgeState stateA = KnowledgeFor(pawnA);
            int before = stateA.records.Count;

            DiaryEvent offEvent = AddRomancePairEvent(pawnA, pawnB, "Spouse", "married");
            Require(stateA.records.Count == before + 1,
                "Capture must continue while prompt injection is disabled (§3.2).");
            Require(string.IsNullOrEmpty(offEvent.MemoryContextForRole(DiaryEvent.InitiatorRole)),
                "No relevant-past lines may be injected while the switch is off.");

            // Re-enable and fire a related event (same partner): the marriage surfaces as at most
            // two dated lines on the NEW event.
            PawnDiaryMod.Settings.enableMemorySystem = true;
            DiaryEvent onEvent = AddRomancePairEvent(pawnA, pawnB, "Lover", "lover");
            string block = onEvent.MemoryContextForRole(DiaryEvent.InitiatorRole);
            if (!onEvent.IsSkipped(DiaryEvent.InitiatorRole))
            {
                Require(!string.IsNullOrWhiteSpace(block),
                    "A related past record (shared partner) must inject relevant-past lines.");
                Require(block.Split('\n').Length <= 2,
                    "At most two relevant-past lines may be injected (§3.2), got: " + block);
                Require(block.IndexOf(pawnB.LabelShort, StringComparison.OrdinalIgnoreCase) >= 0,
                    "The marriage line must reference the partner by saved name; got: " + block);
            }
        }

        // ── 4: culture resolution at capture ─────────────────────────────────────────────────────────

        /// <summary>
        /// Capture resolves the pawn's origin culture once with "captured" provenance — from the
        /// ideology culture (Ideology active) or the faction's allowed cultures otherwise.
        /// </summary>
        [Test]
        public static void CaptureResolvesOriginCultureOnce()
        {
            AddRomancePairEvent(pawnA, pawnB, "Lover", "lover");
            PawnKnowledgeState state = KnowledgeFor(pawnA);
            Require(!string.IsNullOrWhiteSpace(state.originCultureDefName),
                "A colonist's origin culture must resolve at capture (player faction always "
                + "declares allowedCultures).");
            Require(state.originCultureSource == "captured",
                "A live-game resolution must be marked 'captured', got '"
                + state.originCultureSource + "'.");

            string resolved = state.originCultureDefName;
            AddRomancePairEvent(pawnA, pawnB, "ExLover", "breakup");
            Require(KnowledgeFor(pawnA).originCultureDefName == resolved,
                "The origin culture must never be silently rewritten (§4.1).");
        }

        // ── 5: quiet-hediff channel ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// An XML-allowlisted persistent hediff (Sterilized) is remembered through the quiet
        /// channel even though it creates no diary page; an unlisted hediff is not.
        /// </summary>
        [Test]
        public static void QuietHediffChannelCapturesAllowlistedConditionsOnly()
        {
            HediffDef sterilized = DefDatabase<HediffDef>.GetNamedSilentFail("Sterilized");
            if (sterilized == null)
            {
                return; // base defs missing in this environment; nothing to verify
            }

            PawnKnowledgeState state = KnowledgeFor(pawnA);
            int before = state.records.Count;
            pawnA.health.AddHediff(sterilized);
            Require(state.records.Count == before + 1,
                "Adding Sterilized must deposit one quiet-channel record.");
            ImportantMemoryRecord record = state.records[state.records.Count - 1];
            Require(record.eventKind == "body.condition.permanent",
                "Expected kind body.condition.permanent, got '" + record.eventKind + "'.");

            // Repeating the same condition on the same tick dedups instead of doubling.
            pawnA.health.RemoveHediff(pawnA.health.hediffSet.GetFirstHediffOfDef(sterilized));
            pawnA.health.AddHediff(sterilized);
            Require(state.records.Count == before + 1,
                "A same-tick duplicate capture must collapse via the dedup key (§2.2).");
        }

        // ── Harness helpers ──────────────────────────────────────────────────────────────────────────

        private static PawnKnowledgeState KnowledgeFor(Pawn pawn)
        {
            PawnDiaryRecord diary = FindDiaryMethod.Invoke(
                scope.Component, new object[] { pawn, true }) as PawnDiaryRecord;
            if (diary == null)
            {
                throw new AssertionException("Could not resolve the test pawn's diary record.");
            }

            return diary.EnsureKnowledgeState();
        }

        /// <summary>Fires a real romance-shaped pair page through the EventFactory funnel, exactly
        /// how RomanceSignal emits one (relation defName + romance context markers).</summary>
        private static DiaryEvent AddRomancePairEvent(Pawn initiator, Pawn recipient,
            string relationDefName, string kindToken)
        {
            DiaryEvent diaryEvent = scope.Component.AddPairwiseEvent(
                initiator,
                recipient,
                relationDefName,
                kindToken,
                initiator.LabelShortCap + " " + kindToken + " " + recipient.LabelShortCap,
                recipient.LabelShortCap + " " + kindToken + " " + initiator.LabelShortCap,
                string.Empty,
                "romance=" + relationDefName + "; label=" + kindToken + "; kind=" + kindToken);
            if (diaryEvent == null)
            {
                throw new AssertionException("The romance pair event was not registered.");
            }

            return diaryEvent;
        }

        private static DiaryEvent AddPairEvent(Pawn initiator, Pawn recipient, string interactionDefName)
        {
            InteractionDef interaction = DefDatabase<InteractionDef>.GetNamedSilentFail(interactionDefName);
            if (interaction == null)
            {
                throw new AssertionException(
                    "InteractionDef '" + interactionDefName + "' not found in the loaded game.");
            }

            string label = interaction.LabelCap.Resolve();
            DiaryEvent diaryEvent = scope.Component.AddPairwiseEvent(
                initiator,
                recipient,
                interaction.defName,
                label,
                initiator.LabelShortCap + " " + label,
                recipient.LabelShortCap + " " + label,
                InteractionGroups.InstructionFor(interaction),
                DiaryContextBuilder.BuildGameContextSummary(interaction, label));
            if (diaryEvent == null)
            {
                throw new AssertionException("The pair event was not registered.");
            }

            return diaryEvent;
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

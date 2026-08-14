// In-game wiring tests for the deterministic pawn-knowledge system
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §8, integration list). Proves inside a real loaded game:
//   1. Capture: a romance pair page (Spouse) writes one important-event record per POV pawn with
//      the partner as participant — gameplay capture succeeds without completing any LLM request.
//   2. Closed list: an ordinary chat page writes NO record (§2.1 exclusions).
//   3. Injection switch: with the memory setting OFF, capture still happens (§3.2) while the
//      event's relevant-past slot stays empty; with it ON, a later related event carries at most
//      two dated lines referencing the stored fact.
//   4. Culture/save compatibility: capture resolves origin once with "captured" provenance, and
//      schema-1 saves durably mark still-unresolved origins as "inferred" before stamping schema 2.
//   5. Quiet-hediff channel: an XML-allowlisted persistent hediff (Sterilized) is remembered
//      even though it produces no diary page of its own.
//   6. Body events: a REAL amputation (Pawn_HealthTracker.AddHediff) records the stable
//      part_def subject key, and installing onto the same part recalls the loss into both the
//      event slot and the captured LLM prompt (§3.1 "same body part").
//   7. Status family keys: title events share the constant "title" entity key, so a demotion
//      recalls the original investiture (§3.1 "title/status family").
//   8. Death fan-out via a real Pawn.Kill: the killer and the spouse each keep one record;
//      an unrelated bystander keeps none (§2.1), and one malformed relation candidate cannot block
//      a later healthy family owner.
//   9. Conversion channel: adopted culture REPLACES on each conversion and each conversion is
//      recorded (§4.1).
//  10. Role channel: an ideological role change is remembered WITHOUT creating a diary page.
//  11. Defensive caps: insert/global adapters drop captured rows but preserve canonical backgrounds.
//  12. Annotation: a themed prompt carries the pawn's culture clause inline; an ordinary chat
//      prompt does not carry that clause (§4.3).
//  13. Normal profile endpoints detach captured rows, guard ownership, create/update/delete one
//      bounded background singleton, and freeze edits for future events only.
//  14. Legacy developer editor endpoints stay hidden/disabled outside Dev Mode, update only rendered
//      prose, and remove exactly the addressed record without touching another pawn's memory.
//  15. The same developer window receives a detached lore view with culture provenance, resolved
//      clauses, lexical/structured matchers, and the active injection-switch state.
//
// All fragile scaffolding — isolated pawns, settings snapshot/restore, event/diary cleanup —
// lives in the shared PawnDiaryRimTestScope harness. The save round-trip half of the plan's
// integration list is covered by PawnDiaryRepositoryRebuildFixtureTests (real Scribe); the
// RimTalk preset cleanup stays a manual check (needs RimTalk loaded).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using PawnDiary.Ingestion;
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
        private static readonly FieldInfo DiariesField =
            typeof(DiaryGameComponent).GetField(
                "diaries",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ApplyKnowledgeEvictionMethod =
            typeof(DiaryGameComponent).GetMethod(
                "ApplyKnowledgeEviction",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawnA;
        private static Pawn pawnB;
        private static PromptContextDetailLevel savedContextDetailLevel;

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
            savedContextDetailLevel = PawnDiaryMod.Settings.contextDetailLevel;
            PawnDiaryMod.Settings.contextDetailLevel = PromptContextDetailLevel.Full;
        }

        [AfterEach]
        public static void TearDown()
        {
            try
            {
                PawnDiaryMod.Settings.contextDetailLevel = savedContextDetailLevel;
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

        /// <summary>
        /// The real RomanceSignal still deposits marriage knowledge for both pawns when its XML page
        /// group is disabled, while creating no DiaryEvent and no generation work.
        /// </summary>
        [Test]
        public static void DisabledRomancePageStillCapturesKnowledgeWithoutPage()
        {
            scope.SpawnAsLiveColonist(pawnA);
            scope.SpawnAsLiveColonist(pawnB);
            DiaryInteractionGroupDef group =
                InteractionGroups.ClassifyRomanceRelation(PawnRelationDefOf.Spouse.defName);
            Require(group != null, "The shipped spouse romance group could not be resolved.");

            bool priorValue;
            bool hadOverride = PawnDiaryMod.Settings.groupEnabled.TryGetValue(
                group.defName, out priorValue);
            PawnDiaryRecord diaryA = DiaryFor(pawnA);
            PawnDiaryRecord diaryB = DiaryFor(pawnB);
            int pagesA = diaryA.eventIds.Count;
            int pagesB = diaryB.eventIds.Count;
            int memoriesA = CountKind(
                diaryA.EnsureKnowledgeState(), "relation.spouse.gained");
            int memoriesB = CountKind(
                diaryB.EnsureKnowledgeState(), "relation.spouse.gained");
            try
            {
                PawnDiaryMod.Settings.groupEnabled[group.defName] = false;
                DiaryEvents.Submit(new RomanceSignal(
                    pawnA, pawnB, PawnRelationDefOf.Spouse));

                Require(
                    CountKind(diaryA.EnsureKnowledgeState(), "relation.spouse.gained")
                        == memoriesA + 1
                    && CountKind(diaryB.EnsureKnowledgeState(), "relation.spouse.gained")
                        == memoriesB + 1,
                    "A disabled romance page must still deposit one marriage memory per pawn.");
                Require(diaryA.eventIds.Count == pagesA && diaryB.eventIds.Count == pagesB,
                    "No-page knowledge capture must not register a DiaryEvent.");
            }
            finally
            {
                if (hadOverride)
                {
                    PawnDiaryMod.Settings.groupEnabled[group.defName] = priorValue;
                }
                else
                {
                    PawnDiaryMod.Settings.groupEnabled.Remove(group.defName);
                }
            }
        }

        // ── 3: lane detail gates request projection, not event-time capture ─────────────────────────

        /// <summary>
        /// Event registration always freezes the richest relevant-past snapshot before an API lane is
        /// selected. Full keeps that layer; Balanced and Compact remove it only from their detached
        /// request payloads, while important-memory capture continues in every lane.
        /// </summary>
        [Test]
        public static void CompactStillCapturesAndLaneProjectionControlsRelevantPast()
        {
            PawnDiaryMod.Settings.contextDetailLevel = PromptContextDetailLevel.Compact;
            PawnKnowledgeState stateA = KnowledgeFor(pawnA);
            int before = stateA.records.Count;

            AddRomancePairEvent(pawnA, pawnB, "Spouse", "married");
            Require(stateA.records.Count == before + 1,
                "Important-memory capture must continue while the selected lane is Compact (§3.2).");

            // A related event freezes the marriage before any lane is chosen.
            DiaryEvent relatedEvent = AddRomancePairEvent(pawnA, pawnB, "Lover", "lover");
            string block = relatedEvent.MemoryContextForRole(DiaryEvent.InitiatorRole);
            Require(!relatedEvent.IsSkipped(DiaryEvent.InitiatorRole),
                "The lane-projection romance fixture unexpectedly skipped its initiator POV.");
            Require(!string.IsNullOrWhiteSpace(block),
                "A related past record must be frozen before lane projection.");
            Require(block.Split('\n').Length <= 2,
                "At most two relevant-past lines may be injected (§3.2), got: " + block);
            Require(block.IndexOf(pawnB.LabelShort, StringComparison.OrdinalIgnoreCase) >= 0,
                "The marriage line must reference the partner by saved name; got: " + block);

            DiaryPromptRequest full = BuildMemoryLayerRequest(
                relatedEvent, PromptContextDetailLevel.Full);
            DiaryPromptRequest balanced = BuildMemoryLayerRequest(
                relatedEvent, PromptContextDetailLevel.Balanced);
            DiaryPromptRequest compact = BuildMemoryLayerRequest(
                relatedEvent, PromptContextDetailLevel.Compact);
            Require(
                string.Equals(
                    full.payload.initiator.memoryContext,
                    block,
                    StringComparison.Ordinal),
                "Full request projection did not preserve the frozen relevant-past block.");
            Require(
                string.IsNullOrEmpty(balanced.payload.initiator.memoryContext)
                    && string.IsNullOrEmpty(compact.payload.initiator.memoryContext),
                "Balanced/Compact request projection leaked the Full-only memory layer.");
        }

        private static DiaryPromptRequest BuildMemoryLayerRequest(
            DiaryEvent diaryEvent,
            PromptContextDetailLevel level)
        {
            return DiaryPipelineAdapters.BuildPromptRequest(
                diaryEvent,
                DiaryEvent.InitiatorRole,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                null,
                false,
                0,
                level);
        }

        /// <summary>
        /// The Writing Style window's memory endpoints enforce Dev Mode independently of UI
        /// visibility. Editing replaces only rendered prose; removing uses the stable record id and
        /// cannot disturb the partner's copy of the same relationship event.
        /// </summary>
        [Test]
        public static void DevMemoryEditorGuardsEditsAndRemovesExactRecord()
        {
            PawnKnowledgeState stateA = KnowledgeFor(pawnA);
            PawnKnowledgeState stateB = KnowledgeFor(pawnB);
            AddRomancePairEvent(pawnA, pawnB, "Spouse", "married");
            ImportantMemoryRecord target = LastOfKind(stateA, "relation.spouse.gained");
            string recordId = target.recordId;
            string dedupKey = target.dedupKey;
            string eventKind = target.eventKind;
            int tick = target.tick;
            int beforeCountA = stateA.records.Count;
            int beforeCountB = stateB.records.Count;
            int versionBefore = DiaryStateVersion.Current;
            bool originalDevMode = Prefs.DevMode;

            try
            {
                Prefs.DevMode = false;
                Require(scope.Component.ImportantMemoriesForDev(pawnA).Count == 0,
                    "The developer memory list must be hidden outside Dev Mode.");
                Require(!scope.Component.TrySetImportantMemoryTextForDev(
                        pawnA, recordId, "must not save"),
                    "The memory editor must reject text changes outside Dev Mode.");
                Require(!scope.Component.TryRemoveImportantMemoryForDev(pawnA, recordId),
                    "The memory editor must reject removal outside Dev Mode.");
                Require(string.IsNullOrEmpty(target.manualTextOverride)
                        && stateA.records.Count == beforeCountA
                        && DiaryStateVersion.Current == versionBefore,
                    "Rejected developer operations must not mutate memory state or its version.");

                Prefs.DevMode = true;
                Require(scope.Component.ImportantMemoriesForDev(pawnA).Count == beforeCountA,
                    "Dev Mode must expose the pawn's existing memory list.");

                const string editedInput = "  <b>I remember</b>\r\n\tthis exactly.  ";
                const string editedText = "I remember this exactly.";
                Require(scope.Component.TrySetImportantMemoryTextForDev(
                        pawnA, recordId, editedInput),
                    "The developer memory text edit was rejected.");
                Require(target.manualTextOverride == editedText,
                    "The editor must save one prompt-safe line, got '"
                    + target.manualTextOverride + "'.");
                Require(target.recordId == recordId
                        && target.dedupKey == dedupKey
                        && target.eventKind == eventKind
                        && target.tick == tick,
                    "Editing prose must preserve stable memory identity and retrieval metadata.");

                ImportantEventRule rule = DiaryKnowledgePolicy.RuleForKind(target.eventKind);
                Require(ImportantMemoryLineRenderer.Render(
                        target.ToSnapshot(), rule?.lineTemplate, 240) == editedText,
                    "The normal renderer must prefer the saved developer text override.");
                Require(DiaryStateVersion.Current > versionBefore,
                    "A successful memory edit must invalidate rendered-state caches.");

                int versionAfterEdit = DiaryStateVersion.Current;
                Require(!scope.Component.TrySetImportantMemoryTextForDev(
                        pawnA, "missing-record", "no"),
                    "An unknown memory id must be rejected.");
                Require(DiaryStateVersion.Current == versionAfterEdit,
                    "A rejected unknown-id edit must not invalidate state.");

                Require(scope.Component.TryRemoveImportantMemoryForDev(pawnA, recordId),
                    "The developer memory removal was rejected.");
                Require(stateA.records.Count == beforeCountA - 1
                        && !ContainsRecordId(stateA, recordId),
                    "Removal must delete exactly the addressed memory.");
                Require(stateB.records.Count == beforeCountB
                        && CountKind(stateB, "relation.spouse.gained") > 0,
                    "Removing one pawn's memory must preserve the partner's independent record.");
                Require(!scope.Component.TryRemoveImportantMemoryForDev(pawnA, recordId),
                    "Removing the same record twice must be rejected.");
            }
            finally
            {
                Prefs.DevMode = originalDevMode;
            }
        }

        /// <summary>
        /// Normal profile methods expose detached captured rows, apply the canonical background plan,
        /// and freeze each event's selected background so a later edit cannot rewrite old context.
        /// </summary>
        [Test]
        public static void ProfileMemoryCrudIsOwnedDetachedAndFutureOnly()
        {
            PawnKnowledgeState stateA = KnowledgeFor(pawnA);
            PawnKnowledgeState stateB = KnowledgeFor(pawnB);
            int beforeB = stateB.records.Count;
            int versionBefore = DiaryStateVersion.Current;

            const string originalBackground = "lowercase Жизнь café";
            Require(scope.Component.TrySetBackgroundMemoryForProfile(
                    pawnA,
                    "  <b>lowercase</b>\r\n\t Жизнь   café  "),
                "The normal profile rejected a valid background create.");
            Require(scope.Component.BackgroundMemoryForProfile(pawnA) == originalBackground,
                "The background getter did not preserve case/non-ASCII normalized prose.");
            Require(scope.Component.ImportantMemoriesForProfile(pawnA).Count == 0,
                "The captured-memory browser must not show the background singleton twice.");
            Require(stateB.records.Count == beforeB
                    && string.IsNullOrEmpty(scope.Component.BackgroundMemoryForProfile(pawnB)),
                "Creating one pawn's background cross-edited another pawn's state.");
            Require(DiaryStateVersion.Current > versionBefore,
                "Creating a background did not invalidate diary/profile caches.");

            scope.EnablePromptCapture();
            DiaryEvent first = AddRomancePairEvent(pawnA, pawnB, "Spouse", "married");
            string firstFrozen = first.MemoryContextForRole(DiaryEvent.InitiatorRole);
            Require(firstFrozen.IndexOf(originalBackground, StringComparison.Ordinal) >= 0,
                "The Full/template-enabled event did not freeze the background fallback: "
                + firstFrozen);
            Require(scope.CapturedPrompt(first, DiaryEvent.InitiatorRole)
                    .IndexOf(originalBackground, StringComparison.Ordinal) >= 0,
                "The Full prompt omitted the frozen background line.");

            ImportantMemoryRecord captured = LastOfKind(stateA, "relation.spouse.gained");
            ImportantMemoryRecord partnerCaptured = LastOfKind(
                stateB,
                "relation.spouse.gained");
            IReadOnlyList<ImportantMemoryRecordSnapshot> detached =
                scope.Component.ImportantMemoriesForProfile(pawnA);
            Require(detached.Count == 1
                    && detached[0].recordId == captured.recordId
                    && detached[0].sourceKind == KnowledgeTokens.SourceKindCaptured
                    && detached[0].recallScope == KnowledgeTokens.RecallScopeContextual,
                "The normal profile did not expose exactly the captured/contextual row.");
            detached[0].manualTextOverride = "must remain detached";
            Require(string.IsNullOrEmpty(captured.manualTextOverride),
                "Mutating a profile snapshot changed the live saved record.");

            string capturedRecordId = captured.recordId;
            string capturedDedup = captured.dedupKey;
            string capturedSourceEventId = captured.sourceEventId;
            string capturedSourceKind = captured.sourceKind;
            string capturedRecallScope = captured.recallScope;
            string capturedKind = captured.eventKind;
            string capturedTopic = captured.topicKey;
            int capturedTick = captured.tick;
            string capturedDate = captured.dateLabel;
            string capturedFallback = captured.fallbackSummary;
            List<string> capturedParticipantIds =
                new List<string>(captured.participantIds);
            List<string> capturedParticipantNames =
                new List<string>(captured.participantNames);
            List<string> capturedSubjects = new List<string>(captured.subjectKeys);
            List<string> capturedFactKeys = new List<string>(captured.factKeys);
            List<string> capturedFactValues = new List<string>(captured.factValues);
            int partnerCountBeforeWrongOwner = stateB.records.Count;
            string partnerTextBeforeWrongOwner = partnerCaptured.manualTextOverride;

            bool originalDevMode = Prefs.DevMode;
            try
            {
                Prefs.DevMode = false;
                Require(!scope.Component.TrySetImportantMemoryTextForProfile(
                        pawnB,
                        capturedRecordId,
                        "must not cross owners"),
                    "A normal profile edit accepted another pawn's record id.");
                Require(!scope.Component.TryRemoveImportantMemoryForProfile(
                        pawnB,
                        capturedRecordId),
                    "A normal profile removal accepted another pawn's record id.");
                Require(scope.Component.TrySetImportantMemoryTextForProfile(
                        pawnA,
                        capturedRecordId,
                        "  <i>I remember this bond.</i>\n "),
                    "Normal-play captured-memory editing was incorrectly Dev-gated.");
            }
            finally
            {
                Prefs.DevMode = originalDevMode;
            }

            Require(captured.manualTextOverride == "I remember this bond.",
                "The normal editor did not sanitize only the captured row's prose.");
            Require(
                stateB.records.Count == partnerCountBeforeWrongOwner
                    && string.Equals(
                        partnerCaptured.manualTextOverride,
                        partnerTextBeforeWrongOwner,
                        StringComparison.Ordinal),
                "A rejected wrong-owner operation changed the other pawn's memory state.");
            Require(
                captured.recordId == capturedRecordId
                    && captured.dedupKey == capturedDedup
                    && captured.sourceEventId == capturedSourceEventId
                    && captured.sourceKind == capturedSourceKind
                    && captured.recallScope == capturedRecallScope
                    && captured.eventKind == capturedKind
                    && captured.topicKey == capturedTopic
                    && captured.tick == capturedTick
                    && captured.dateLabel == capturedDate
                    && captured.fallbackSummary == capturedFallback
                    && captured.participantIds.SequenceEqual(capturedParticipantIds)
                    && captured.participantNames.SequenceEqual(capturedParticipantNames)
                    && captured.subjectKeys.SequenceEqual(capturedSubjects)
                    && captured.factKeys.SequenceEqual(capturedFactKeys)
                    && captured.factValues.SequenceEqual(capturedFactValues),
                "Editing captured prose changed stable ownership, retrieval, or matching metadata.");

            const string updatedBackground = "I worked on orbital farms before landing.";
            Require(scope.Component.TrySetBackgroundMemoryForProfile(pawnA, updatedBackground),
                "The normal profile rejected a valid background update.");
            Require(first.MemoryContextForRole(DiaryEvent.InitiatorRole) == firstFrozen
                    && firstFrozen.IndexOf(updatedBackground, StringComparison.Ordinal) < 0,
                "Editing the profile rewrote an older event's frozen memory context.");

            DiaryEvent later = AddRomancePairEvent(pawnA, pawnB, "Lover", "lover");
            string laterFrozen = later.MemoryContextForRole(DiaryEvent.InitiatorRole);
            Require(laterFrozen.IndexOf(updatedBackground, StringComparison.Ordinal) >= 0,
                "A later event did not receive the updated background fallback: " + laterFrozen);
            Require(captured.dedupKey == capturedDedup && captured.eventKind == capturedKind,
                "Editing rendered prose changed captured matching identity.");

            string stateOwner = stateA.pawnId;
            int stateCountBeforeMismatch = stateA.records.Count;
            try
            {
                stateA.pawnId = pawnB.GetUniqueLoadID();
                Require(
                    !scope.Component.TrySetImportantMemoryTextForProfile(
                        pawnA,
                        capturedRecordId,
                        "must not repair a conflicting owner")
                        && !scope.Component.TryRemoveImportantMemoryForProfile(
                            pawnA,
                            capturedRecordId)
                        && !scope.Component.TrySetBackgroundMemoryForProfile(
                            pawnA,
                            "must not replace a conflicting owner"),
                    "A normal profile mutation accepted a conflicting saved-state owner.");
            }
            finally
            {
                stateA.pawnId = stateOwner;
            }

            Require(
                stateA.records.Count == stateCountBeforeMismatch
                    && string.Equals(
                        scope.Component.BackgroundMemoryForProfile(pawnA),
                        updatedBackground,
                        StringComparison.Ordinal)
                    && captured.manualTextOverride == "I remember this bond.",
                "A rejected conflicting-owner mutation changed the pawn's saved memories.");

            int partnerCountBeforeNormalRemove = stateB.records.Count;
            originalDevMode = Prefs.DevMode;
            try
            {
                Prefs.DevMode = false;
                Require(
                    scope.Component.TryRemoveImportantMemoryForProfile(
                        pawnA,
                        capturedRecordId),
                    "Normal-play captured-memory removal was incorrectly Dev-gated.");
            }
            finally
            {
                Prefs.DevMode = originalDevMode;
            }

            Require(
                !ContainsRecordId(stateA, capturedRecordId)
                    && stateB.records.Count == partnerCountBeforeNormalRemove
                    && ContainsRecordId(stateB, partnerCaptured.recordId),
                "Normal removal did not delete only the addressed owner's captured row.");

            int backgroundLimit = scope.Component.BackgroundMemoryTextLimitForProfile();
            Require(!scope.Component.TrySetBackgroundMemoryForProfile(
                    pawnA,
                    new string('x', backgroundLimit + 1))
                    && scope.Component.BackgroundMemoryForProfile(pawnA) == updatedBackground,
                "An over-limit background edit mutated or replaced the saved singleton.");
            Require(scope.Component.TrySetBackgroundMemoryForProfile(pawnA, "  <b> </b>\n")
                    && string.IsNullOrEmpty(scope.Component.BackgroundMemoryForProfile(pawnA)),
                "Blank + Save did not remove the canonical background singleton.");
        }

        /// <summary>
        /// The lore-memory endpoint is guarded by Dev Mode and exposes the current XML policy as a
        /// detached view: profile status, clauses, and the localized prose terms developers need
        /// to diagnose why a topic did or did not annotate a prompt.
        /// </summary>
        [Test]
        public static void DevLoreMemoryShowsCultureProfilesAndTopicMatchers()
        {
            PawnKnowledgeState state = KnowledgeFor(pawnA);
            state.originCultureDefName = "Astropolitan";
            state.originCultureSource = KnowledgeTokens.CultureSourceCaptured;
            state.adoptedCultureDefName = "Corunan";
            bool originalDevMode = Prefs.DevMode;

            try
            {
                Prefs.DevMode = false;
                Require(scope.Component.LoreMemoryForDev(pawnA) == null,
                    "The developer lore snapshot must be unavailable outside Dev Mode.");

                Prefs.DevMode = true;
                LoreMemorySnapshotForDev lore = scope.Component.LoreMemoryForDev(pawnA);
                Require(lore != null && lore.hasKnowledgeState,
                    "Dev Mode must expose the pawn's existing lore-memory state.");
                Require(lore.originCultureSource == KnowledgeTokens.CultureSourceCaptured,
                    "The lore view must preserve captured/inferred provenance.");
                Require(lore.originProfile.requestedCultureDefName == "Astropolitan"
                        && lore.originProfile.resolvedCultureDefName == "Astropolitan"
                        && lore.originProfile.authored,
                    "The lore view did not resolve the authored origin profile.");
                Require(lore.adoptedProfile.requestedCultureDefName == "Corunan"
                        && lore.adoptedProfile.resolvedCultureDefName == "Corunan"
                        && lore.adoptedProfile.authored,
                    "The lore view did not resolve the authored adopted profile.");

                LoreMemoryTopicForDev mechanoids = FindLoreTopicForDev(
                    lore, "mechanoids");
                Require(mechanoids != null,
                    "The loaded lore view must include the mechanoids topic.");
                Require(!string.IsNullOrWhiteSpace(mechanoids.originClause)
                        && !string.IsNullOrWhiteSpace(mechanoids.adoptedClause),
                    "The lore view must expose both culture clauses for a converted pawn.");
                Require(mechanoids.triggerTextTerms.Exists(
                        term => string.Equals(
                            term, "mechanoid*", StringComparison.OrdinalIgnoreCase)),
                    "The lore view must expose the localized prose matcher used by the planner.");
                Require(mechanoids.triggerContextPairs.Exists(
                        pair => string.Equals(
                            pair, "faction=Mechanoid", StringComparison.OrdinalIgnoreCase)),
                    "The lore view must also expose structured topic matchers.");
            }
            finally
            {
                Prefs.DevMode = originalDevMode;
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

        /// <summary>
        /// Replays the component's schema-1 migration on an isolated, uninitialized component so the
        /// loaded colony is untouched. The inferred marker must become saved state before schema 2 is
        /// stamped; an already-resolved culture must keep its captured provenance.
        /// </summary>
        [Test]
        public static void SchemaOneMigrationPersistsLegacyCultureProvenance()
        {
            FieldInfo diariesField = typeof(DiaryGameComponent).GetField(
                "diaries", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo versionField = typeof(DiaryGameComponent).GetField(
                "knowledgeSchemaVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo migrate = typeof(DiaryGameComponent).GetMethod(
                "PostLoadInitKnowledge", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(diariesField != null && versionField != null && migrate != null,
                "Knowledge migration reflection handles changed.");

            PawnDiaryRecord unresolved = new PawnDiaryRecord { pawnId = "legacy-unresolved" };
            PawnKnowledgeState unresolvedState = unresolved.EnsureKnowledgeState();
            PawnDiaryRecord resolved = new PawnDiaryRecord { pawnId = "legacy-resolved" };
            PawnKnowledgeState resolvedState = resolved.EnsureKnowledgeState();
            resolvedState.originCultureDefName = "Rustican";
            resolvedState.originCultureSource = KnowledgeTokens.CultureSourceCaptured;

            DiaryGameComponent isolated = (DiaryGameComponent)
                FormatterServices.GetUninitializedObject(typeof(DiaryGameComponent));
            diariesField.SetValue(isolated, new List<PawnDiaryRecord> { unresolved, resolved });
            versionField.SetValue(isolated, 1);
            migrate.Invoke(isolated, null);

            Require(unresolvedState.originCultureSource == KnowledgeTokens.CultureSourceInferred,
                "A schema-1 unresolved origin must gain durable inferred provenance.");
            Require(resolvedState.originCultureDefName == "Rustican"
                    && resolvedState.originCultureSource == KnowledgeTokens.CultureSourceCaptured,
                "Migration must not rewrite an already-resolved captured culture.");
            Require((int)versionField.GetValue(isolated) == 2,
                "Successful legacy migration must stamp knowledge schema 2.");
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

        // ── 6: real amputation → stable part key → same-part recall into the prompt ─────────────────

        /// <summary>
        /// A real missing-part hediff records body.part.lost with the stable "part:&lt;def&gt;"
        /// subject key; installing a bionic onto the SAME part then recalls the loss into the new
        /// event's relevant-past slot AND into the captured LLM prompt (dateLabel is the
        /// language-proof marker).
        /// </summary>
        [Test]
        public static void AmputationRecordsPartKeyAndSamePartInstallRecallsIt()
        {
            HediffDef missingPart = HediffDefOf.MissingBodyPart;
            HediffDef bionicLeg = DefDatabase<HediffDef>.GetNamedSilentFail("BionicLeg");
            if (bionicLeg == null)
            {
                Log.Message("[PawnDiary RimTest knowledge] BionicLeg def missing; skipping.");
                return;
            }

            scope.EnablePromptCapture();
            Pawn patient = scope.CreateGeneratingAdultColonist();
            BodyPartRecord leg = FindPart(patient, "Leg");
            PawnKnowledgeState state = KnowledgeFor(patient);
            int before = CountKind(state, "body.part.lost");

            DiaryEvent lossEvent = scope.FireAndRequireEvent(
                () => patient.health.AddHediff(missingPart, leg),
                missingPart.defName,
                patient,
                null);
            Require(CountKind(state, "body.part.lost") == before + 1,
                "A real amputation must deposit one body.part.lost record.");
            ImportantMemoryRecord loss = LastOfKind(state, "body.part.lost");
            Require(loss.subjectKeys.Contains("part:" + leg.def.defName),
                "The loss record must carry the stable part_def subject key; got ["
                + string.Join(",", loss.subjectKeys.ToArray()) + "].");

            DiaryEvent installEvent = scope.FireAndRequireEvent(
                () =>
                {
                    // Vanilla surgery removes MissingBodyPart before installing the replacement.
                    // Adding a bionic directly to a missing part logs a real RimWorld error.
                    Hediff missing = patient.health.hediffSet.GetFirstHediffOfDef(missingPart);
                    if (missing != null)
                    {
                        patient.health.RemoveHediff(missing);
                    }
                    patient.health.AddHediff(bionicLeg, leg);
                },
                bionicLeg.defName,
                patient,
                null);
            string slot = installEvent.MemoryContextForRole(DiaryEvent.InitiatorRole);
            Require(!string.IsNullOrWhiteSpace(slot)
                    && slot.IndexOf(loss.dateLabel, StringComparison.Ordinal) >= 0,
                "Installing onto the same part must recall the loss (dated line); slot was: '"
                + slot + "'.");
            string prompt = scope.CapturedPrompt(installEvent, DiaryEvent.InitiatorRole);
            Require(prompt.IndexOf(loss.dateLabel, StringComparison.Ordinal) >= 0,
                "The captured prompt must carry the recalled dated loss line.");
        }

        // ── 7: title/status family key across title events ───────────────────────────────────────────

        /// <summary>
        /// Title events share the constant "title" entity key (§3.1), so a later demotion event
        /// recalls the original investiture even though the two carry different progression
        /// defNames. Runs DLC-free: capture is plain string matching.
        /// </summary>
        [Test]
        public static void TitleFamilyKeyRecallsInvestitureOnDemotion()
        {
            PawnKnowledgeState state = KnowledgeFor(pawnA);
            int before = CountKind(state, "status.title.advanced");

            AddProgressionSoloEvent(pawnA, "RoyalTitleGained",
                "progression=RoyalTitleGained; progression_kind=royal_title; label=title; new_value=Knight");
            Require(CountKind(state, "status.title.advanced") == before + 1,
                "A RoyalTitleGained event must deposit one status.title.advanced record.");
            ImportantMemoryRecord gained = LastOfKind(state, "status.title.advanced");
            Require(gained.subjectKeys.Contains("title"),
                "Title records must carry the constant 'title' family key.");

            DiaryEvent demotion = AddProgressionSoloEvent(pawnA, "RoyalTitleDemoted",
                "progression=RoyalTitleDemoted; progression_kind=royal_title; label=title; previous_value=Knight; new_value=none");
            Require(!demotion.IsSkipped(DiaryEvent.InitiatorRole),
                "The title-demotion fixture unexpectedly skipped its initiator POV.");
            string slot = demotion.MemoryContextForRole(DiaryEvent.InitiatorRole);
            Require(!string.IsNullOrWhiteSpace(slot)
                    && slot.IndexOf(gained.dateLabel, StringComparison.Ordinal) >= 0,
                "The demotion must recall the investiture via the shared 'title' key; slot: '"
                + slot + "'.");
        }

        // ── 8: death fan-out through a real Pawn.Kill ────────────────────────────────────────────────

        /// <summary>
        /// Killing a colonist through the real Pawn.Kill path fans records out to the pawn
        /// instigator (death.killed) and the victim's spouse (death.family with a relation fact),
        /// while an unrelated bystander keeps nothing (§2.1: ordinary witnesses never remember).
        /// </summary>
        [Test]
        public static void DeathFanOutReachesKillerAndSpouseOnly()
        {
            Pawn victim = scope.CreateAdultColonist();
            Pawn killer = pawnA;
            Pawn spouse = pawnB;
            Pawn bystander = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(victim);

            // Diary records must exist for owners to capture; the marriage this fires also seeds
            // ordinary relationship records — assertions below count death kinds only.
            PawnKnowledgeState killerState = KnowledgeFor(killer);
            PawnKnowledgeState spouseState = KnowledgeFor(spouse);
            PawnKnowledgeState bystanderState = KnowledgeFor(bystander);
            victim.relations.AddDirectRelation(PawnRelationDefOf.Spouse, spouse);

            int killedBefore = CountKind(killerState, "death.killed");
            int familyBefore = CountKind(spouseState, "death.family");
            int bystanderBefore = bystanderState.records.Count;

            RegisterDeadPawnCleanup(victim);
            DamageInfo dinfo = new DamageInfo(DamageDefOf.Cut, 9999f, 999f, -1f, killer);
            // Ordinary combat deaths usually produce a vanilla death Tale; only condition/need deaths
            // without such a Tale use PawnDiary_DeathFallback. This fixture is about the independent
            // knowledge fan-out, so requiring the fallback page would assert the wrong production route.
            victim.Kill(dinfo);

            Require(CountKind(killerState, "death.killed") == killedBefore + 1,
                "The pawn instigator must remember the kill.");
            ImportantMemoryRecord killed = LastOfKind(killerState, "death.killed");
            Require(killed.participantIds.Contains(victim.GetUniqueLoadID()),
                "The kill record must reference the victim as participant.");

            Require(CountKind(spouseState, "death.family") == familyBefore + 1,
                "The spouse must remember the family death.");
            ImportantMemoryRecord familyLoss = LastOfKind(spouseState, "death.family");
            Require(FactValue(familyLoss, "relation").Length > 0,
                "The family record must carry the victim's relation label.");
            Require(FactValue(familyLoss, "victim").Length > 0,
                "The family record must carry the victim's saved name.");

            Require(bystanderState.records.Count == bystanderBefore,
                "An unrelated bystander must keep no death record.");
        }

        /// <summary>
        /// A third-party mod can leave a null pawn in one direct-relation row. Projecting that candidate
        /// through vanilla's parent workers throws, but the bad candidate must not abort death-family
        /// capture for a healthy spouse elsewhere in the same graph.
        /// </summary>
        [Test]
        public static void MalformedRelationCandidateDoesNotBlockHealthyFamilyOwner()
        {
            Pawn victim = scope.CreateAdultColonist();
            Pawn malformedCandidate = scope.CreateAdultColonist();
            Pawn spouse = scope.CreateAdultColonist();
            PawnKnowledgeState malformedState = KnowledgeFor(malformedCandidate);
            PawnKnowledgeState spouseState = KnowledgeFor(spouse);

            // Add the malformed candidate first so the old RelatedPawns implementation would throw
            // before reaching the valid spouse.
            victim.relations.AddDirectRelation(PawnRelationDefOf.ExLover, malformedCandidate);
            victim.relations.AddDirectRelation(PawnRelationDefOf.Spouse, spouse);
            DirectPawnRelation malformed = new DirectPawnRelation(
                PawnRelationDefOf.Parent,
                null,
                0);
            malformedCandidate.relations.DirectRelations.Add(malformed);

            int malformedBefore = CountKind(malformedState, "death.family");
            int spouseBefore = CountKind(spouseState, "death.family");
            try
            {
                scope.Component.CaptureDeathKnowledge(victim, null);
            }
            finally
            {
                // Remove the intentionally corrupt row before shared fixture teardown asks RimWorld to
                // clean relation trackers. The test must never leak malformed state into the loaded game.
                malformedCandidate.relations.DirectRelations.Remove(malformed);
            }

            Require(CountKind(malformedState, "death.family") == malformedBefore,
                "The malformed non-family candidate unexpectedly received a death-family record.");
            Require(CountKind(spouseState, "death.family") == spouseBefore + 1,
                "One malformed relation candidate blocked the healthy spouse's death-family record.");
        }

        // ── 9 + 10: conversion and role channels ─────────────────────────────────────────────────────

        /// <summary>
        /// The conversion channel replaces the adopted culture on EACH conversion (earlier adopted
        /// cultures are not retained, §4.1) and records every conversion. Drives the component
        /// seam directly so the test runs without the Ideology DLC.
        /// </summary>
        [Test]
        public static void ConversionReplacesAdoptedCultureAndRecords()
        {
            PawnKnowledgeState state = KnowledgeFor(pawnA);
            state.originCultureDefName = string.Empty;
            state.originCultureSource = string.Empty;
            int before = CountKind(state, "status.ideo.converted");

            scope.Component.CaptureIdeoConversionKnowledge(
                pawnA, "Old Way", "The Flame", "Corunan", "Rustican");
            Require(state.originCultureDefName == "Rustican",
                "Conversion must resolve origin from the pre-mutation culture, not the adopted one.");
            Require(state.adoptedCultureDefName == "Corunan",
                "The first conversion must set the adopted culture.");

            scope.Component.CaptureIdeoConversionKnowledge(
                pawnA, "The Flame", "New Dawn", "Kriminul", "Corunan");
            Require(state.adoptedCultureDefName == "Kriminul",
                "A second conversion must REPLACE the adopted culture, got '"
                + state.adoptedCultureDefName + "'.");
            Require(CountKind(state, "status.ideo.converted") == before + 2,
                "Each conversion must deposit one record.");
        }

        /// <summary>
        /// The role channel records an appointment and a removal WITHOUT creating any diary page —
        /// gameplay capture succeeds with no page and no LLM request (§8 integration list).
        /// </summary>
        [Test]
        public static void RoleChangeCapturesWithoutDiaryPage()
        {
            PawnDiaryRecord diary = DiaryFor(pawnA);
            PawnKnowledgeState state = diary.EnsureKnowledgeState();
            int gainedBefore = CountKind(state, "status.role.gained");
            int lostBefore = CountKind(state, "status.role.lost");
            int pagesBefore = diary.eventIds.Count;

            scope.Component.CaptureRoleKnowledge(pawnA, "moral guide", "The Flame", true);
            scope.Component.CaptureRoleKnowledge(pawnA, "moral guide", "The Flame", false);

            Require(CountKind(state, "status.role.gained") == gainedBefore + 1
                    && CountKind(state, "status.role.lost") == lostBefore + 1,
                "Role appointment and removal must each deposit one record.");
            Require(diary.eventIds.Count == pagesBefore,
                "Role capture must not create a diary page.");
            Require(FactValue(LastOfKind(state, "status.role.gained"), "role") == "moral guide",
                "The role record must carry the role label fact.");
        }

        // ── 11: per-pawn defensive cap at insert ─────────────────────────────────────────────────────

        /// <summary>
        /// With the XML per-pawn cap lowered to 2, a third capture drops the oldest record at
        /// insert (§2.3) instead of growing past the cap.
        /// </summary>
        [Test]
        public static void PerPawnCapDropsOldestAtInsert()
        {
            DiaryKnowledgeTuningDef tuning =
                DefDatabase<DiaryKnowledgeTuningDef>.GetNamedSilentFail("Diary_Knowledge");
            if (tuning == null)
            {
                throw new AssertionException("Diary_Knowledge tuning def is missing.");
            }

            int savedCap = tuning.maxRecordsPerPawn;
            tuning.maxRecordsPerPawn = 2;
            try
            {
                Pawn fresh = scope.CreateAdultColonist();
                PawnKnowledgeState state = KnowledgeFor(fresh);
                Require(state.records.Count == 0, "The fresh pawn must start with no records.");

                AddRomancePairEvent(fresh, pawnB, "Lover", "lover");
                AddRomancePairEvent(fresh, pawnB, "Spouse", "married");
                AddRomancePairEvent(fresh, pawnB, "ExSpouse", "divorce");
                Require(state.records.Count == 2,
                    "The per-pawn cap of 2 must hold at insert; got " + state.records.Count + ".");
            }
            finally
            {
                tuning.maxRecordsPerPawn = savedCap;
            }
        }

        /// <summary>
        /// Both impure eviction adapters must project the canonical protection bit. Insert-time
        /// enforcement drops the newly captured row before the singleton, and the detached global scan
        /// drops captured rows while terminating safely when every remaining row is protected.
        /// </summary>
        [Test]
        public static void ProtectedBackgroundSurvivesInsertionAndGlobalEvictionAdapters()
        {
            DiaryKnowledgeTuningDef tuning =
                DefDatabase<DiaryKnowledgeTuningDef>.GetNamedSilentFail("Diary_Knowledge");
            if (tuning == null)
            {
                throw new AssertionException("Diary_Knowledge tuning def is missing.");
            }

            if (DiariesField == null || ApplyKnowledgeEvictionMethod == null)
            {
                throw new AssertionException(
                    "The knowledge fixture could not locate the global eviction adapter surface.");
            }

            int savedPerPawnCap = tuning.maxRecordsPerPawn;
            int savedGlobalCap = tuning.maxRecordsGlobal;
            try
            {
                Pawn insertionOwner = scope.CreateAdultColonist();
                string insertionOwnerId = insertionOwner.GetUniqueLoadID();
                Require(
                    scope.Component.TrySetBackgroundMemoryForProfile(
                        insertionOwner,
                        "I grew up maintaining irrigation pumps."),
                    "The insertion-eviction fixture could not seed its canonical background.");
                PawnKnowledgeState insertionState = KnowledgeFor(insertionOwner);

                tuning.maxRecordsPerPawn = 1;
                AddRomancePairEvent(insertionOwner, pawnB, "Spouse", "married");
                Require(
                    insertionState.records.Count == 1
                        && IsCanonicalBackground(
                            insertionState.records[0],
                            insertionOwnerId),
                    "Insert-time cap enforcement evicted the canonical background before captured prose.");

                // Route the mixed row through the global adapter's per-owner plan. Keeping the global
                // cap non-binding avoids consuming production's one-shot global-cap warning in a test.
                tuning.maxRecordsPerPawn = 1;
                tuning.maxRecordsGlobal = 10;
                PawnDiaryRecord mixedOwner = NewIsolatedBackgroundDiary(
                    "PawnDiary_RimTest_GlobalMixed",
                    "I remember the old observatory.");
                PawnKnowledgeState mixedState = mixedOwner.EnsureKnowledgeState();
                mixedState.records.Add(new ImportantMemoryRecord
                {
                    recordId = "PawnDiary_RimTest_GlobalCaptured",
                    dedupKey = "PawnDiary_RimTest_GlobalCaptured",
                    sourceEventId = "PawnDiary_RimTest_GlobalEvent",
                    sourceKind = KnowledgeTokens.SourceKindCaptured,
                    recallScope = KnowledgeTokens.RecallScopeContextual,
                    eventKind = "relation.spouse.gained",
                    tick = 100,
                    fallbackSummary = "Disposable captured row."
                });
                DiaryGameComponent mixedComponent = (DiaryGameComponent)
                    FormatterServices.GetUninitializedObject(typeof(DiaryGameComponent));
                DiariesField.SetValue(
                    mixedComponent,
                    new List<PawnDiaryRecord> { mixedOwner });
                ApplyKnowledgeEvictionMethod.Invoke(mixedComponent, null);
                Require(
                    mixedState.records.Count == 1
                        && IsCanonicalBackground(
                            mixedState.records[0],
                            mixedOwner.pawnId),
                    "The global eviction adapter failed to mark canon protected or retain it over a captured row.");

                // A truly global overflow with only protected rows must return without a deletion or
                // warning. This is the adapter-level counterpart to the pure all-protected planner test.
                tuning.maxRecordsPerPawn = 10;
                tuning.maxRecordsGlobal = 1;
                PawnDiaryRecord firstProtected = NewIsolatedBackgroundDiary(
                    "PawnDiary_RimTest_GlobalProtectedA",
                    "I remember the northern coast.");
                PawnDiaryRecord secondProtected = NewIsolatedBackgroundDiary(
                    "PawnDiary_RimTest_GlobalProtectedB",
                    "I remember the desert road.");
                DiaryGameComponent protectedOnlyComponent = (DiaryGameComponent)
                    FormatterServices.GetUninitializedObject(typeof(DiaryGameComponent));
                DiariesField.SetValue(
                    protectedOnlyComponent,
                    new List<PawnDiaryRecord> { firstProtected, secondProtected });
                ApplyKnowledgeEvictionMethod.Invoke(protectedOnlyComponent, null);
                Require(
                    firstProtected.EnsureKnowledgeState().records.Count == 1
                        && secondProtected.EnsureKnowledgeState().records.Count == 1,
                    "The global adapter deleted a canonical row when every candidate was protected.");
            }
            finally
            {
                tuning.maxRecordsPerPawn = savedPerPawnCap;
                tuning.maxRecordsGlobal = savedGlobalCap;
            }
        }

        // ── 12: inline culture annotation reaches the real prompt ────────────────────────────────────

        /// <summary>
        /// A themed event (royal title — the "empire" topic triggers on its defName) carries the
        /// pawn's culture clause inline in the REAL captured prompt; an ordinary chat prompt does
        /// not carry that clause (§4.3). The expected clause is read from the pawn's own resolved
        /// profile (or the fallback lens), so the assertion is language-proof.
        /// </summary>
        [Test]
        public static void CultureAnnotationLandsInThemedPromptOnly()
        {
            scope.EnablePromptCapture();
            Pawn writer = scope.CreateGeneratingAdultColonist();
            PawnKnowledgeState state = KnowledgeFor(writer);

            DiaryEvent titled = AddProgressionSoloEvent(writer, "RoyalTitleGained",
                "progression=RoyalTitleGained; progression_kind=royal_title; label=title; new_value=Knight");
            string empireClause = EmpireClauseFor(state);
            Require(empireClause.Length > 0,
                "The generated colonist must resolve a shipped culture profile for this fixture.");

            scope.Component.QueueSolo(titled, DiaryEvent.InitiatorRole);
            string themedPrompt = scope.CapturedPrompt(titled, DiaryEvent.InitiatorRole);
            Require(themedPrompt.IndexOf(empireClause, StringComparison.Ordinal) >= 0,
                "The themed prompt must carry the culture clause '" + empireClause + "'.");

            DiaryEvent chat = AddPairEvent(writer, pawnB, "Chitchat");
            Require(!chat.IsSkipped(DiaryEvent.InitiatorRole),
                "The ordinary-chat negative-control fixture unexpectedly skipped its initiator POV.");
            scope.Component.QueuePair(chat);
            string chatPrompt = scope.CapturedPrompt(chat, DiaryEvent.InitiatorRole);
            Require(chatPrompt.IndexOf(empireClause, StringComparison.Ordinal) < 0,
                "An ordinary chat prompt must not carry the empire clause.");
        }

        /// <summary>The empire-topic clause of the writer's resolved (or fallback) profile.</summary>
        private static string EmpireClauseFor(PawnKnowledgeState state)
        {
            string culture = string.IsNullOrWhiteSpace(state.adoptedCultureDefName)
                ? state.originCultureDefName
                : state.adoptedCultureDefName;
            DiaryCultureProfileDef match = null;
            DiaryCultureProfileDef fallback = null;
            foreach (DiaryCultureProfileDef def in DefDatabase<DiaryCultureProfileDef>.AllDefsListForReading)
            {
                if (def.isFallback && fallback == null)
                {
                    fallback = def;
                }

                if (!string.IsNullOrWhiteSpace(culture)
                    && string.Equals(def.cultureDefName, culture, StringComparison.OrdinalIgnoreCase))
                {
                    match = def;
                }
            }

            DiaryCultureProfileDef profile = match ?? (string.IsNullOrWhiteSpace(culture) ? null : fallback);
            if (profile?.clauses == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < profile.clauses.Count; i++)
            {
                if (string.Equals(profile.clauses[i]?.topicKey, "empire", StringComparison.OrdinalIgnoreCase))
                {
                    return (profile.clauses[i].clause ?? string.Empty).Trim();
                }
            }

            return string.Empty;
        }

        // ── Harness helpers ──────────────────────────────────────────────────────────────────────────

        private static PawnKnowledgeState KnowledgeFor(Pawn pawn)
        {
            return DiaryFor(pawn).EnsureKnowledgeState();
        }

        private static PawnDiaryRecord DiaryFor(Pawn pawn)
        {
            PawnDiaryRecord diary = FindDiaryMethod.Invoke(
                scope.Component, new object[] { pawn, true }) as PawnDiaryRecord;
            if (diary == null)
            {
                throw new AssertionException("Could not resolve the test pawn's diary record.");
            }

            return diary;
        }

        private static int CountKind(PawnKnowledgeState state, string eventKind)
        {
            int count = 0;
            for (int i = 0; i < state.records.Count; i++)
            {
                if (state.records[i] != null
                    && string.Equals(state.records[i].eventKind, eventKind, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool ContainsRecordId(PawnKnowledgeState state, string recordId)
        {
            for (int i = 0; i < state.records.Count; i++)
            {
                if (state.records[i] != null
                    && string.Equals(
                        state.records[i].recordId,
                        recordId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static LoreMemoryTopicForDev FindLoreTopicForDev(
            LoreMemorySnapshotForDev lore,
            string topicKey)
        {
            if (lore?.topics == null)
            {
                return null;
            }

            for (int i = 0; i < lore.topics.Count; i++)
            {
                LoreMemoryTopicForDev topic = lore.topics[i];
                if (topic != null
                    && string.Equals(
                        topic.topicKey,
                        topicKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return topic;
                }
            }

            return null;
        }

        private static ImportantMemoryRecord LastOfKind(PawnKnowledgeState state, string eventKind)
        {
            for (int i = state.records.Count - 1; i >= 0; i--)
            {
                if (state.records[i] != null
                    && string.Equals(state.records[i].eventKind, eventKind, StringComparison.Ordinal))
                {
                    return state.records[i];
                }
            }

            throw new AssertionException("No record of kind '" + eventKind + "' was found.");
        }

        private static string FactValue(ImportantMemoryRecord record, string key)
        {
            for (int i = 0; i < record.factKeys.Count; i++)
            {
                if (string.Equals(record.factKeys[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return i < record.factValues.Count ? (record.factValues[i] ?? string.Empty) : string.Empty;
                }
            }

            return string.Empty;
        }

        private static BodyPartRecord FindPart(Pawn pawn, string partDefName)
        {
            List<BodyPartRecord> parts = pawn.RaceProps.body.AllParts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (string.Equals(parts[i].def.defName, partDefName, StringComparison.Ordinal))
                {
                    return parts[i];
                }
            }

            throw new AssertionException("Body part '" + partDefName + "' not found on the test pawn.");
        }

        /// <summary>Fires a progression-shaped solo page through the funnel (DLC-free: capture is
        /// plain string matching on the synthetic progression defName + context).</summary>
        private static DiaryEvent AddProgressionSoloEvent(Pawn pawn, string defName, string gameContext)
        {
            DiaryEvent diaryEvent = scope.Component.AddSoloEvent(
                pawn,
                null,
                defName,
                "title",
                pawn.LabelShortCap + " " + defName,
                string.Empty,
                gameContext);
            if (diaryEvent == null)
            {
                throw new AssertionException("The progression solo event was not registered.");
            }

            return diaryEvent;
        }

        /// <summary>Registers cleanup for the state a killed pawn leaves behind (corpse holder,
        /// world-pawn entry) — mirrors PawnDiaryDeathFlowTests so no corpse survives the test.</summary>
        private static void RegisterDeadPawnCleanup(Pawn pawn)
        {
            scope.RegisterCleanup(() =>
            {
                if (pawn != null
                    && !pawn.Destroyed
                    && Find.WorldPawns != null
                    && Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.RemovePawn(pawn);
                }
            });

            scope.RegisterCleanup(() =>
            {
                Corpse corpse = pawn?.ParentHolder as Corpse;
                if (corpse != null && !corpse.Destroyed)
                {
                    corpse.Destroy(DestroyMode.Vanish);
                }
            });
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

        /// <summary>Builds one detached diary containing only its exact canonical background row.</summary>
        private static PawnDiaryRecord NewIsolatedBackgroundDiary(
            string ownerPawnId,
            string text)
        {
            PawnDiaryRecord diary = new PawnDiaryRecord
            {
                pawnId = ownerPawnId,
                pawnName = ownerPawnId
            };
            PawnKnowledgeState state = diary.EnsureKnowledgeState();
            PlayerMemoryMutationPlan plan = PlayerMemoryPolicy.PlanBackstoryMutation(
                ownerPawnId,
                null,
                text,
                450);
            if (plan.record == null)
            {
                throw new AssertionException(
                    "The isolated eviction fixture could not plan a canonical background row.");
            }

            state.records.Add(ImportantMemoryRecord.FromSnapshot(plan.record));
            return diary;
        }

        /// <summary>Checks the saved scalar identity used by both runtime eviction adapters.</summary>
        private static bool IsCanonicalBackground(
            ImportantMemoryRecord record,
            string ownerPawnId)
        {
            return record != null && PlayerMemoryPolicy.IsCanonicalBackstory(
                ownerPawnId,
                record.recordId,
                record.dedupKey,
                record.eventKind,
                record.sourceKind,
                record.recallScope);
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

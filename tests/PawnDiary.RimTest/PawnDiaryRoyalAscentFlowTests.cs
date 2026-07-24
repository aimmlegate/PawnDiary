// Loaded-game acceptance for Royalty Phase 7 Royal Ascent. The canonical Quest.Accept/Quest.End
// callbacks are driven on controlled, unregistered quests; mapless stable-witness fanout is observed
// with exact event/dedup ownership restored afterward; and append-only active-window identity is
// round-tripped through RimWorld's real Scribe. A restored per-pawn generation gate prevents requests.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Proves Royal Ascent hooks, ownership, closure, truth, persistence, and DLC gates.</summary>
    [TestSuite]
    public static class PawnDiaryRoyalAscentFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly FieldInfo ActiveWindowsField =
            typeof(DiaryGameComponent).GetField("activeEventWindows", PrivateInstance);
        private static readonly FieldInfo RecentWindowEventsField =
            typeof(DiaryGameComponent).GetField("recentEventWindowEvents", PrivateInstance);
        private static readonly FieldInfo RecentEventsField =
            typeof(DiaryGameComponent).GetField("recentEvents", PrivateInstance);
        private static readonly FieldInfo KnownAcceptedQuestIdsField =
            typeof(DiaryGameComponent).GetField("knownAcceptedQuestIds", PrivateInstance);
        private static readonly FieldInfo EventsField =
            typeof(DiaryGameComponent).GetField("events", PrivateInstance);
        private static readonly FieldInfo UnclaimedRoyalMutationsField =
            typeof(RoyaltyTransientState).GetField("unclaimedMutations", PrivateStatic);
        private static readonly MethodInfo ActivePromptCandidatesMethod =
            typeof(DiaryGameComponent).GetMethod("ActiveEventWindowPromptCandidates", PrivateInstance);
        private static readonly MethodInfo ArcCandidateFromEventMethod =
            typeof(DiaryGameComponent).GetMethod("ArcCandidateFromEvent", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;
        private static RoyaltyPolicySnapshot livePolicy;
        private static bool originalPolicyEnabled;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(
                "questRoyalAscent", "questCompleted", "questFailed", "reflection");
            pawn = scope.CreateAdultColonist();
            livePolicy = DiaryRoyaltyPolicy.Snapshot();
            originalPolicyEnabled = livePolicy.enabled;
            livePolicy.enabled = true;
            DiaryTuningDef tuning = DiaryTuning.Current;
            bool originalArcReflection = tuning.arcReflectionEnabled;
            tuning.arcReflectionEnabled = true;
            scope.RegisterCleanup(() => tuning.arcReflectionEnabled = originalArcReflection);
            RequireReflectionState();
            RegisterTransientStoreCleanup();
        }

        [AfterEach]
        public static void TearDown()
        {
            try { scope?.TearDown(); }
            finally
            {
                if (livePolicy != null) livePolicy.enabled = originalPolicyEnabled;
                scope = null;
                pawn = null;
                livePolicy = null;
            }
        }

        /// <summary>Royalty-on/off Def availability and Prompt Studio visibility share one package gate.</summary>
        [Test]
        public static void RoyaltyPackageAndPromptStudioAvailabilityMatch()
        {
            DiaryInteractionGroupDef group = DefDatabase<DiaryInteractionGroupDef>
                .GetNamedSilentFail("questRoyalAscent");
            DiaryEventWindowDef window = DefDatabase<DiaryEventWindowDef>.GetNamedSilentFail("RoyalAscent");
            DiaryEventPromptDef prompt = DefDatabase<DiaryEventPromptDef>
                .GetNamedSilentFail("DiaryEventPrompt_RoyalAscent");
            PawnDiaryRimTestScope.Require(group != null && window != null && prompt != null,
                "Royal Ascent group/window/prompt Defs were not loaded.");
            PawnDiaryRimTestScope.Require(group.questFanoutScope == QuestFanoutScope.MapWitness,
                "Royal Ascent did not load its one-witness Quest fanout policy.");
            PawnDiaryRimTestScope.Require(
                group.UnavailableForCurrentRuntime() == !ModsConfig.RoyaltyActive
                && window.MissingRequiredPackage() == !ModsConfig.RoyaltyActive
                && prompt.MissingRequiredPackage() == !ModsConfig.RoyaltyActive,
                "Royal Ascent package availability did not match ModsConfig.RoyaltyActive.");

            MethodInfo method = typeof(PawnDiaryMod).GetMethod(
                "EventPromptDefsForSettings", BindingFlags.Static | BindingFlags.NonPublic);
            List<DiaryEventPromptDef> visible = method?.Invoke(null, null) as List<DiaryEventPromptDef>;
            bool shown = visible != null && visible.Any(def => def?.defName == prompt.defName);
            PawnDiaryRimTestScope.Require(shown == ModsConfig.RoyaltyActive,
                "Royal Ascent Prompt Studio visibility did not match the active Royalty package.");

            if (!ModsConfig.RoyaltyActive)
            {
                int eventCount = EventCount();
                int windowCount = ActiveWindows().Count;
                Quest syntheticDlcRoot = BuildAscentQuest("Royalty-inactive no-op fixture");
                syntheticDlcRoot.Accept(null);
                syntheticDlcRoot.End(QuestEndOutcome.Success, false, false);
                PawnDiaryRimTestScope.Require(EventCount() == eventCount
                        && ActiveWindows().Count == windowCount,
                    "Royalty-inactive exact-root callbacks created a page or active window.");
            }
            else
            {
                int eventCount = EventCount();
                int windowCount = ActiveWindows().Count;
                livePolicy.enabled = false;
                Quest masterOff = BuildAscentQuest("Royal Ascent master-off no-op fixture");
                masterOff.Accept(null);
                masterOff.End(QuestEndOutcome.Success, false, false);
                PawnDiaryRimTestScope.Require(EventCount() == eventCount
                        && ActiveWindows().Count == windowCount,
                    "Royalty master-off exact-root callbacks created a page or active window.");
                livePolicy.enabled = true;
            }
        }

        /// <summary>The exact installed Quest overloads retain Pawn Diary's prefix/postfix owners.</summary>
        [Test]
        public static void ExactQuestLifecycleHarmonyTargetsAreRegistered()
        {
            RequireOwnedQuestPatches(
                AccessTools.DeclaredMethod(typeof(Quest), nameof(Quest.Accept), new[] { typeof(Pawn) }),
                typeof(QuestAcceptPatch));
            RequireOwnedQuestPatches(
                AccessTools.DeclaredMethod(typeof(Quest), nameof(Quest.End), new[]
                {
                    typeof(QuestEndOutcome), typeof(bool), typeof(bool)
                }),
                typeof(QuestEndPatch));
        }

        /// <summary>Real Accept starts one bounded window; real End closes it and repeated calls do nothing.</summary>
        [Test]
        public static void RealQuestCallbacksStartAndCloseOneWindow()
        {
            if (!RequireRoyaltyOrSkip(nameof(RealQuestCallbacksStartAndCloseOneWindow))) return;
            Quest quest = BuildAscentQuest("A controlled ascent lifecycle");
            Pawn expectedWitness = DiaryGameComponent.StableLoadedMapWitness();
            PawnDiaryRimTestScope.Require(expectedWitness != null,
                "Royal Ascent start acceptance requires one eligible stable witness.");
            // This real hook assigns its page to a player's loaded-map colonist. Suppress transport,
            // own every exact page for teardown, and isolate only the dedup entries this synthetic
            // quest can touch. A recent real Quest page for the same witness must not make the fixture
            // order/environment-dependent, and the original entry is restored byte-for-byte afterward.
            scope.SuppressDiaryGenerationForTest(expectedWitness);
            scope.OwnDiaryEventsCreatedAfterThisPoint();
            IsolateRecentEventKey(GenericEventTypeDedup.KeyFor(
                DiaryEventType.Quest,
                CaptureDecision.GenerateSolo,
                expectedWitness.GetUniqueLoadID()));
            IsolateRecentEventKey("quest|" + quest.id + "|accepted");
            IsolateRecentEventKey("quest|" + quest.id + "|completed");
            HashSet<ActiveEventWindowState> before = new HashSet<ActiveEventWindowState>(ActiveWindows());
            int exactQuestPagesBeforeAccept = EventCount("EndGame_RoyalAscent");

            DiaryEvent startPage = scope.FireAndRequireEvent(
                () => quest.Accept(null), "RoyalAscent", expectedWitness, null);
            ActiveEventWindowState active = ActiveWindows().FirstOrDefault(row => row != null
                && !before.Contains(row) && row.windowDefName == "RoyalAscent");
            PawnDiaryRimTestScope.Require(active != null,
                "The real Quest.Accept callback did not start the Royal Ascent window.");
            PawnDiaryRimTestScope.Require(active.mapUniqueId == -1
                    && active.startCorrelationId == quest.GetUniqueLoadID()
                    && active.startNarrativeArcKey == "royalty-ascent|" + quest.GetUniqueLoadID()
                    && active.expiresTick > active.startedTick,
                "The Royal Ascent start window did not persist bounded mapless identity.");
            PawnDiaryRimTestScope.Require(EventCount("EndGame_RoyalAscent") == exactQuestPagesBeforeAccept,
                "Quest.Accept created a second Quest-domain Royal Ascent page beside the start window.");
            DiaryInteractionGroupDef ascentGroup = DefDatabase<DiaryInteractionGroupDef>
                .GetNamedSilentFail("questRoyalAscent");
            DiaryEntryView startView = startPage.ToViewFor(expectedWitness.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(ascentGroup != null && startView != null
                    && startView.GroupLabel == ascentGroup.label && startView.ColorCue == "royalty",
                "The saved Royal Ascent start page did not recover its exact group for display.");
            List<NarrativeEvidence> startEvidence =
                startPage.NarrativeEvidenceForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(startEvidence.Count == 1
                    && startEvidence[0].phase == RoyalAscentPhaseTokens.Started
                    && startEvidence[0].arcKey == active.startNarrativeArcKey,
                "The start page did not freeze the active window's exact journey evidence.");

            RoyaltyNarrativeSnapshot snapshot =
                scope.Component.RoyaltyNarrativeSnapshotFor(expectedWitness, startPage.tick);
            List<NarrativeLensCandidate> pressure =
                RoyaltyNarrativeProvider.Build(startEvidence, snapshot);
            PawnDiaryRimTestScope.Require(pressure.Count(row =>
                    row.category == NarrativeCategoryTokens.Pressure
                    && row.arcKey == active.startNarrativeArcKey) == 1,
                "The active window did not expose one exact-arc court-pressure candidate.");

            RequirePromptContribution(active, expectedWitness, true,
                "A valid active Royal Ascent did not contribute its XML prompt-pressure candidate.");
            string savedCorrelationId = active.startCorrelationId;
            string savedArcKey = active.startNarrativeArcKey;
            active.startCorrelationId = string.Empty;
            active.startNarrativeArcKey = string.Empty;
            RequirePromptContribution(active, expectedWitness, false,
                "An identity-less Royal Ascent window still altered prompt selection.");
            active.startCorrelationId = savedCorrelationId;
            active.startNarrativeArcKey = savedArcKey;
            livePolicy.enabled = false;
            try
            {
                RequirePromptContribution(active, expectedWitness, false,
                    "A Royal Ascent window still altered prompts while the Royalty master was disabled.");
            }
            finally
            {
                livePolicy.enabled = true;
            }

            int afterStartCount = EventCount();
            quest.Accept(null);
            PawnDiaryRimTestScope.Require(EventCount() == afterStartCount,
                "A repeated Quest.Accept created a competing Royal Ascent page.");

            DiaryEvent terminalPage = scope.FireAndRequireEvent(
                () => quest.End(QuestEndOutcome.Success, false, false),
                "EndGame_RoyalAscent", expectedWitness, null);
            PawnDiaryRimTestScope.Require(!ActiveWindows().Contains(active),
                "The real Quest.End success callback did not close the matching window.");
            PawnDiaryRimTestScope.Require(
                terminalPage.gameContext.Contains("quest_signal=completed"),
                "The real Quest.End page did not preserve the proven success outcome.");
            int afterEndCount = EventCount();
            quest.End(QuestEndOutcome.Success, false, false);
            PawnDiaryRimTestScope.Require(EventCount() == afterEndCount,
                "A repeated Quest.End created a competing Royal Ascent page.");
        }

        /// <summary>A terminal signal for another quest instance cannot steal the active chapter.</summary>
        [Test]
        public static void QuestInstanceCorrelationOwnsWindowClosure()
        {
            if (!RequireRoyaltyOrSkip(nameof(QuestInstanceCorrelationOwnsWindowClosure))) return;
            PawnDiaryMod.Settings.SetGroupEnabled("questRoyalAscent", false);
            string arc = "royalty-ascent|Quest_7001";
            scope.Component.RecordEventWindowSignal("Quest", "EndGame_RoyalAscent", "accepted",
                "Royal Ascent", null, null, "Quest_7001", arc);
            ActiveEventWindowState active = ActiveWindows().LastOrDefault(row => row?.windowDefName == "RoyalAscent");
            PawnDiaryRimTestScope.Require(active != null, "Controlled Royal Ascent start did not open a window.");

            scope.Component.RecordEventWindowSignal("Quest", "EndGame_RoyalAscent", "failed",
                "Royal Ascent", null, null, "Quest_7002", "royalty-ascent|Quest_7002");
            PawnDiaryRimTestScope.Require(ActiveWindows().Contains(active),
                "A mismatched quest instance incorrectly closed the active Royal Ascent.");
            scope.Component.RecordEventWindowSignal("Quest", "EndGame_RoyalAscent", "failed",
                "Royal Ascent", null, null, string.Empty, string.Empty);
            PawnDiaryRimTestScope.Require(ActiveWindows().Contains(active),
                "An empty terminal correlation incorrectly closed an identified Royal Ascent.");
            scope.Component.RecordEventWindowSignal("Quest", "EndGame_RoyalAscent", "failed",
                "Royal Ascent", null, null, "Quest_7001", arc);
            PawnDiaryRimTestScope.Require(!ActiveWindows().Contains(active),
                "The matching quest instance did not close the active Royal Ascent.");
        }

        /// <summary>Exact Ascent fanout selects one stable owner; ordinary Quest fanout remains unchanged.</summary>
        [Test]
        public static void ExactFanoutUsesOneStableWitnessAndOrdinaryQuestUsesAllEligible()
        {
            if (!RequireRoyaltyOrSkip(nameof(ExactFanoutUsesOneStableWitnessAndOrdinaryQuestUsesAllEligible))) return;
            DiaryInteractionGroupDef acceptedGroup = DefDatabase<DiaryInteractionGroupDef>
                .GetNamedSilentFail("questAccepted");
            PawnDiaryRimTestScope.Require(acceptedGroup != null,
                "The exact-classifier fixture could not find the generic accepted Quest group.");
            bool originalCatchAll = acceptedGroup.catchAll;
            try
            {
                acceptedGroup.catchAll = true;
                DiaryInteractionGroupDef completed = InteractionGroups.ClassifyQuest(
                    "PawnDiaryTest_OrdinaryQuest", QuestEventData.SignalCompleted);
                PawnDiaryRimTestScope.Require(completed?.defName == "questCompleted",
                    "An unrelated Quest catch-all stole exact-root classification before signal fallback.");
            }
            finally
            {
                acceptedGroup.catchAll = originalCatchAll;
            }

            Pawn expectedWitness = DiaryGameComponent.StableLoadedMapWitness();
            PawnDiaryRimTestScope.Require(expectedWitness != null,
                "Stable-witness acceptance requires an eligible colonist on a loaded colony map.");
            QuestFanoutSignal ascent = new QuestFanoutSignal(
                BuildAscentQuest("One exact witness"), QuestEventData.SignalCompleted,
                "PawnDiary.Event.QuestCompleted");
            List<DiarySignal> ascentChildren = ascent.PerPawnSignals().ToList();
            PawnDiaryRimTestScope.Require(ascentChildren.Count == 1
                    && ascentChildren[0].Payload.PawnId == expectedWitness.GetUniqueLoadID(),
                "Royal Ascent fanout did not choose the deterministic loaded-map witness.");

            Quest ordinaryQuest = BuildQuest("Ordinary completed quest", "PawnDiaryTest_OrdinaryQuest");
            QuestFanoutSignal ordinary = new QuestFanoutSignal(
                ordinaryQuest, QuestEventData.SignalCompleted, "PawnDiary.Event.QuestCompleted");
            int expected = LoadedEligiblePawnIds().Count;
            PawnDiaryRimTestScope.Require(expected > 0 && ordinary.PerPawnSignals().Count() == expected,
                "The default Quest fanout no longer preserved all eligible loaded-map colonists.");
        }

        /// <summary>Completion/failure pages preserve only the terminal Quest truth and shared journey arc.</summary>
        [Test]
        public static void TerminalPagesCarryTruthfulOutcomeAndJourneyEvidence()
        {
            if (!RequireRoyaltyOrSkip(nameof(TerminalPagesCarryTruthfulOutcomeAndJourneyEvidence))) return;
            DiaryInteractionGroupDef ascentGroup = DefDatabase<DiaryInteractionGroupDef>
                .GetNamedSilentFail("questRoyalAscent");
            PawnDiaryRimTestScope.Require(ascentGroup != null,
                "The terminal display fixture could not find the exact Royal Ascent group.");
            string[] signals = { QuestEventData.SignalCompleted, QuestEventData.SignalFailed };
            string[] textKeys = { "PawnDiary.Event.QuestCompleted", "PawnDiary.Event.QuestFailed" };
            for (int i = 0; i < signals.Length; i++)
            {
                Pawn writer = i == 0 ? pawn : scope.CreateAdultColonist();
                Quest quest = BuildAscentQuest("Terminal fixture " + i);
                QuestFanoutSignal fanout = new QuestFanoutSignal(quest, signals[i], textKeys[i]);
                DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                    () => DiaryEvents.Submit(new QuestPawnSignal(
                        fanout, writer, writer.GetUniqueLoadID())),
                    "EndGame_RoyalAscent", writer, null);
                PawnDiaryRimTestScope.Require(
                    diaryEvent.gameContext.Contains("quest_signal=" + signals[i])
                    && !diaryEvent.gameContext.Contains(quest.GetUniqueLoadID()),
                    "Royal Ascent terminal context lost its outcome or leaked correlation identity.");
                string prose = (diaryEvent.neutralText ?? string.Empty).ToLowerInvariant();
                PawnDiaryRimTestScope.Require(!prose.Contains("stellarch arrived")
                        && !prose.Contains("boarded") && !prose.Contains("escaped"),
                    "Royal Ascent terminal source prose claimed an unproved arrival/escape fact.");
                List<NarrativeEvidence> evidence =
                    diaryEvent.NarrativeEvidenceForRole(DiaryEvent.InitiatorRole);
                PawnDiaryRimTestScope.Require(evidence.Count == 1
                        && evidence[0].facet == NarrativeFacetTokens.JourneyChapter
                        && evidence[0].phase == signals[i]
                        && evidence[0].arcKey == "royalty-ascent|" + quest.GetUniqueLoadID()
                        && evidence[0].salience == NarrativeSalienceTokens.Terminal,
                    "Royal Ascent terminal page did not freeze its exact shared journey evidence.");
                scope.RequirePendingMajorArc(writer, diaryEvent.eventId);
                DiaryEntryView view = diaryEvent.ToViewFor(writer.GetUniqueLoadID());
                ArcMemoryCandidate arcCandidate = ArcCandidateFromEvent(diaryEvent, writer);
                PawnDiaryRimTestScope.Require(view != null
                        && view.GroupLabel == ascentGroup.label
                        && view.ColorCue == "royalty"
                        && diaryEvent.ToneDirective() == ascentGroup.tone,
                    "A saved Royal Ascent terminal page fell back to generic Quest display policy.");
                PawnDiaryRimTestScope.Require(arcCandidate?.groupKey == "questRoyalAscent",
                    "Royal Ascent reflection memory was bucketed with generic Quest outcomes.");
            }
        }

        /// <summary>Append-only correlation fields survive Scribe; missing legacy fields default empty.</summary>
        [Test]
        public static void ActiveWindowIdentityRoundTripsAndLegacyDefaultsStayEmpty()
        {
            ActiveEventWindowState loaded = ScribeRoundTrip(new ActiveEventWindowState
            {
                windowDefName = "RoyalAscent",
                windowKey = "RoyalAscent",
                startedTick = 100,
                expiresTick = 1200100,
                mapUniqueId = -1,
                startSource = "Quest",
                startSignal = "accepted",
                startDefName = "EndGame_RoyalAscent",
                startCorrelationId = "Quest_41",
                startNarrativeArcKey = "royalty-ascent|Quest_41"
            });
            PawnDiaryRimTestScope.Require(loaded.startCorrelationId == "Quest_41"
                    && loaded.startNarrativeArcKey == "royalty-ascent|Quest_41",
                "Royal Ascent active-window identity did not survive real Scribe.");

            ActiveEventWindowState legacy = ScribeRoundTrip(new ActiveEventWindowState
            {
                windowDefName = "RoyalAscent",
                windowKey = "RoyalAscent",
                startDefName = "EndGame_RoyalAscent"
            });
            PawnDiaryRimTestScope.Require(legacy.startCorrelationId == string.Empty
                    && legacy.startNarrativeArcKey == string.Empty,
                "Missing Phase-7 save fields did not migrate to conservative empty defaults.");
        }

        /// <summary>FinalizeInit discards process-static Royalty ownership across game boundaries.</summary>
        [Test]
        public static void FinalizeInitResetsRoyaltyTransientState()
        {
            IList pending = UnclaimedRoyalMutationsField?.GetValue(null) as IList;
            PawnDiaryRimTestScope.Require(pending != null,
                "The Royalty transient reset fixture could not locate its process-static store.");
            scope.RegisterCleanup(RoyaltyTransientState.Reset);
            pending.Add(null);
            scope.Component.FinalizeInit();
            PawnDiaryRimTestScope.Require(pending.Count == 0,
                "FinalizeInit left process-static Royalty ownership across a game boundary.");
        }

        private static Quest BuildAscentQuest(string name)
        {
            return BuildQuest(name, "EndGame_RoyalAscent");
        }

        private static Quest BuildQuest(string name, string rootDefName)
        {
            return new Quest
            {
                id = Find.UniqueIDsManager.GetNextQuestID(),
                name = name,
                description = "A controlled loaded-game quest lifecycle fixture.",
                root = new QuestScriptDef { defName = rootDefName, label = "Royal Ascent" }
            };
        }

        private static HashSet<string> LoadedEligiblePawnIds()
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                List<Pawn> colonists = maps[i]?.mapPawns?.FreeColonists;
                for (int j = 0; j < (colonists == null ? 0 : colonists.Count); j++)
                {
                    Pawn candidate = colonists[j];
                    if (DiaryGameComponent.IsDiaryEligible(candidate)) result.Add(candidate.GetUniqueLoadID());
                }
            }
            return result;
        }

        private static void RequireOwnedQuestPatches(MethodBase target, Type patchType)
        {
            Patches patches = target == null ? null : Harmony.GetPatchInfo(target);
            PawnDiaryRimTestScope.Require(target != null && patches != null
                    && patches.Prefixes.Any(row => row.owner == "aimml.pawndiary"
                        && row.PatchMethod?.DeclaringType == patchType && row.PatchMethod.Name == "Prefix")
                    && patches.Postfixes.Any(row => row.owner == "aimml.pawndiary"
                        && row.PatchMethod?.DeclaringType == patchType && row.PatchMethod.Name == "Postfix"),
                "Expected Pawn Diary's guarded Quest prefix/postfix on " + target + ".");
        }

        private static void RequireReflectionState()
        {
            PawnDiaryRimTestScope.Require(ActiveWindowsField != null && RecentWindowEventsField != null
                    && RecentEventsField != null && KnownAcceptedQuestIdsField != null && EventsField != null
                    && UnclaimedRoyalMutationsField != null && ActivePromptCandidatesMethod != null
                    && ArcCandidateFromEventMethod != null,
                "Royal Ascent fixtures could not locate component lifecycle stores.");
        }

        /// <summary>
        /// Temporarily removes one exact dispatcher-dedup entry and restores the player's prior value
        /// after the fixture. Any replacement written by the synthetic quest is removed first.
        /// </summary>
        private static void IsolateRecentEventKey(string key)
        {
            IDictionary recent = RecentEventsField.GetValue(scope.Component) as IDictionary;
            PawnDiaryRimTestScope.Require(recent != null && !string.IsNullOrEmpty(key),
                "Royal Ascent fixture could not isolate its exact dispatcher dedup key.");
            bool existed = recent.Contains(key);
            object original = existed ? recent[key] : null;
            recent.Remove(key);
            scope.RegisterCleanup(() =>
            {
                recent.Remove(key);
                if (existed) recent[key] = original;
            });
        }

        private static List<ActiveEventWindowState> ActiveWindows()
        {
            return ActiveWindowsField.GetValue(scope.Component) as List<ActiveEventWindowState>;
        }

        private static HashSet<int> KnownAcceptedQuestIds()
        {
            return KnownAcceptedQuestIdsField.GetValue(scope.Component) as HashSet<int>;
        }

        private static int EventCount()
        {
            DiaryEventRepository repository =
                EventsField.GetValue(scope.Component) as DiaryEventRepository;
            return repository?.AllEvents.Count ?? -1;
        }

        private static int EventCount(string defName)
        {
            DiaryEventRepository repository =
                EventsField.GetValue(scope.Component) as DiaryEventRepository;
            return repository?.AllEvents.Count(row => row != null
                && string.Equals(row.interactionDefName, defName, StringComparison.Ordinal)) ?? -1;
        }

        private static ArcMemoryCandidate ArcCandidateFromEvent(DiaryEvent diaryEvent, Pawn writer)
        {
            return ArcCandidateFromEventMethod.Invoke(
                scope.Component,
                new object[]
                {
                    diaryEvent,
                    writer.GetUniqueLoadID(),
                    DiaryEvent.InitiatorRole,
                    0,
                    0
                }) as ArcMemoryCandidate;
        }

        private static List<PromptEnchantmentCandidate> ActivePromptCandidates(
            Pawn subject, out float normalWeightMultiplier)
        {
            object[] arguments = { subject, 1f };
            List<PromptEnchantmentCandidate> result = ActivePromptCandidatesMethod.Invoke(
                scope.Component, arguments) as List<PromptEnchantmentCandidate>;
            normalWeightMultiplier = arguments[1] is float ? (float)arguments[1] : 1f;
            PawnDiaryRimTestScope.Require(result != null,
                "Royal Ascent fixture could not collect active event-window prompt candidates.");
            return result;
        }

        /// <summary>
        /// Compares prompt selection with and without one exact active row, leaving unrelated loaded
        /// windows untouched. Both candidate count and the normal-candidate weight must move together.
        /// </summary>
        private static void RequirePromptContribution(
            ActiveEventWindowState active, Pawn subject, bool expected, string message)
        {
            List<ActiveEventWindowState> windows = ActiveWindows();
            int index = windows.IndexOf(active);
            PawnDiaryRimTestScope.Require(index >= 0,
                "Royal Ascent prompt fixture lost its controlled active window.");

            List<PromptEnchantmentCandidate> baseline;
            float baselineMultiplier;
            windows.RemoveAt(index);
            try
            {
                baseline = ActivePromptCandidates(subject, out baselineMultiplier);
            }
            finally
            {
                windows.Insert(index, active);
            }

            float activeMultiplier;
            List<PromptEnchantmentCandidate> withActive =
                ActivePromptCandidates(subject, out activeMultiplier);
            bool candidateMatches = expected
                ? withActive.Count == baseline.Count + 1
                : withActive.Count == baseline.Count;
            bool multiplierMatches = expected
                ? Math.Abs(activeMultiplier - baselineMultiplier) > 0.0001f
                : Math.Abs(activeMultiplier - baselineMultiplier) <= 0.0001f;
            PawnDiaryRimTestScope.Require(candidateMatches && multiplierMatches, message);
        }

        private static void RegisterTransientStoreCleanup()
        {
            List<ActiveEventWindowState> windows = ActiveWindows();
            List<ActiveEventWindowState> baselineWindows = windows == null
                ? new List<ActiveEventWindowState>()
                : new List<ActiveEventWindowState>(windows);
            // A legitimate loaded save may already be inside Royal Ascent. Temporarily remove that row
            // so restartOnStart=false cannot make this synthetic fixture environment-dependent; the exact
            // original list and object identities are restored in teardown.
            windows?.RemoveAll(row => row != null && string.Equals(
                row.windowDefName, RoyalAscentPolicy.WindowDefName, StringComparison.OrdinalIgnoreCase));
            IDictionary recent = RecentWindowEventsField.GetValue(scope.Component) as IDictionary;
            HashSet<object> baselineKeys = new HashSet<object>();
            if (recent != null) foreach (object key in recent.Keys) baselineKeys.Add(key);
            HashSet<int> baselineAccepted = new HashSet<int>(KnownAcceptedQuestIds());
            scope.RegisterCleanup(() =>
            {
                List<ActiveEventWindowState> liveWindows = ActiveWindows();
                if (liveWindows != null)
                {
                    liveWindows.Clear();
                    liveWindows.AddRange(baselineWindows);
                }
                IDictionary liveRecent = RecentWindowEventsField.GetValue(scope.Component) as IDictionary;
                if (liveRecent != null)
                {
                    List<object> added = new List<object>();
                    foreach (object key in liveRecent.Keys)
                        if (!baselineKeys.Contains(key)) added.Add(key);
                    for (int i = 0; i < added.Count; i++) liveRecent.Remove(added[i]);
                }
                HashSet<int> liveAccepted = KnownAcceptedQuestIds();
                liveAccepted.Clear();
                foreach (int id in baselineAccepted) liveAccepted.Add(id);
            });
        }

        private static ActiveEventWindowState ScribeRoundTrip(ActiveEventWindowState original)
        {
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath,
                "PawnDiary_RoyalAscent_" + Guid.NewGuid().ToString("N") + ".xml");
            ActiveEventWindowState saveRef = original;
            ActiveEventWindowState loaded = null;
            try
            {
                Scribe.saver.InitSaving(path, "root");
                Scribe_Deep.Look(ref saveRef, "window");
                Scribe.saver.FinalizeSaving();
                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "window");
                Scribe.loader.FinalizeLoading();
                Scribe.mode = LoadSaveMode.Inactive;
                return loaded;
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        private static bool RequireRoyaltyOrSkip(string fixtureName)
        {
            if (ModsConfig.RoyaltyActive) return true;
            Log.Message("[Pawn Diary RimTest] SKIP " + fixtureName
                + ": Royalty is not active in this test profile.");
            return false;
        }
    }
}

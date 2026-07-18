// Loaded-game acceptance for Royalty Phase 4 title/psylink correctness. These fixtures mutate only
// disposable pawns and drive the exact hook coordinator, faction-aware fallback scanner, ritual
// ownership bridge, neuroformer owner, and delayed title-thought release through production code.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Proves Phase-4 loaded title/psylink ownership, fallback, and replay invariants.</summary>
    [TestSuite]
    public static class PawnDiaryRoyaltyProgressionFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo EventsField =
            typeof(DiaryGameComponent).GetField("events", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(
                "progressionRoyalTitle", "progressionPsylink", "ritualRoyal",
                "thoughtPositive", "thoughtNegative");
            RoyaltyTransientState.Reset();
            ForceSignalEnabled(DiarySignalPolicies.Progression);
            ForceSignalEnabled(DiarySignalPolicies.Thought);
            pawn = scope.CreateAdultColonist();
        }

        [AfterEach]
        public static void TearDown()
        {
            try { scope?.TearDown(); }
            finally
            {
                RoyaltyTransientState.Reset();
                scope = null;
                pawn = null;
            }
        }

        /// <summary>
        /// Uses vanilla SetTitle twice and then removes the title. The private exact callback must emit
        /// promotion/loss with the Empire identity and exact before/after facts, while the slow scanner
        /// sees already-advanced observations and cannot duplicate either action.
        /// </summary>
        [Test]
        public static void RealTitlePromotionAndLossKeepExactFactionWithoutScannerDuplicates()
        {
            if (!RequireRoyaltyOrSkip(nameof(RealTitlePromotionAndLossKeepExactFactionWithoutScannerDuplicates)))
                return;
            Faction faction = RequireEmpire();
            RoyalTitleDef lower;
            RoyalTitleDef higher;
            RequireTitlePair(faction, out lower, out higher);
            RegisterRoyalCleanup(pawn, faction);

            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", false);
            pawn.royalty.SetTitle(faction, lower, false, false, false);
            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", true);

            DiaryEvent promotion = scope.FireAndRequireEvent(
                () => pawn.royalty.SetTitle(faction, higher, false, false, false),
                ProgressionEventData.RoyalTitlePromotedDefName,
                pawn,
                null);
            RequireContext(promotion, "royal_transition=promotion");
            RequireContext(promotion, "royal_faction_id=" + faction.GetUniqueLoadID());
            RequireContext(promotion, "previous_title_def=" + lower.defName);
            RequireContext(promotion, "title_def=" + higher.defName);
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));

            DiaryEvent loss = scope.FireAndRequireEvent(
                () => pawn.royalty.SetTitle(faction, null, false, false, false),
                ProgressionEventData.RoyalTitleLostDefName,
                pawn,
                null);
            RequireContext(loss, "royal_transition=loss");
            RequireContext(loss, "royal_faction_id=" + faction.GetUniqueLoadID());
            RequireContext(loss, "previous_title_def=" + higher.defName);
            RequireContext(loss, "title_def=none");
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
        }

        /// <summary>
        /// Models a missing/private-hook or modded direct mutation by changing the live title list.
        /// The scanner must baseline the exact faction silently and later report its disappearance as
        /// RoyalTitleLost, preserving the disappeared faction and title facts.
        /// </summary>
        [Test]
        public static void FactionScannerFallsBackToExactTitleLoss()
        {
            if (!RequireRoyaltyOrSkip(nameof(FactionScannerFallsBackToExactTitleLoss))) return;
            Faction faction = RequireEmpire();
            RoyalTitleDef lower;
            RoyalTitleDef ignored;
            RequireTitlePair(faction, out lower, out ignored);
            RoyalTitle row = AddRoyalTitleDirectly(pawn, faction, lower);
            scope.RegisterCleanup(() => pawn?.royalty?.AllTitlesForReading?.Remove(row));

            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
            pawn.royalty.AllTitlesForReading.Remove(row);
            DiaryEvent loss = scope.FireAndRequireEvent(
                () => scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false),
                ProgressionEventData.RoyalTitleLostDefName,
                pawn,
                null);
            RequireContext(loss, "royal_faction_id=" + faction.GetUniqueLoadID());
            RequireContext(loss, "previous_title_def=" + lower.defName);
        }

        /// <summary>Observation while output is disabled advances truth and cannot replay on re-enable.</summary>
        [Test]
        public static void DisabledThenEnabledDoesNotCatchUpTitleMutation()
        {
            if (!RequireRoyaltyOrSkip(nameof(DisabledThenEnabledDoesNotCatchUpTitleMutation))) return;
            Faction faction = RequireEmpire();
            RoyalTitleDef lower;
            RoyalTitleDef ignored;
            RequireTitlePair(faction, out lower, out ignored);

            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, false, false));
            RoyalTitle row = AddRoyalTitleDirectly(pawn, faction, lower);
            scope.RegisterCleanup(() => pawn?.royalty?.AllTitlesForReading?.Remove(row));
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, false, false));
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
        }

        /// <summary>
        /// A bestowing batch containing both title and psylink changes belongs to one enriched target
        /// ritual page. The exact title callback, immediate completion, and later scanner emit no
        /// separate RoyalTitle/Psylink progression page.
        /// </summary>
        [Test]
        public static void BestowingClaimsTitleAndPsylinkAsOneEnrichedRitual()
        {
            if (!RequireRoyaltyOrSkip(nameof(BestowingClaimsTitleAndPsylinkAsOneEnrichedRitual))) return;
            Faction faction = RequireEmpire();
            RoyalTitleDef title;
            RoyalTitleDef ignored;
            RequireTitlePair(faction, out title, out ignored);
            RegisterRoyalCleanup(pawn, faction);
            Pawn bestower = scope.CreateTrackedPawn(PawnKindDefOf.Colonist, faction);

            RoyalMutationBatchSnapshot batch = scope.Component.BeginRoyalMutationCause(
                pawn, faction, RoyalMutationCauseTokens.ImperialBestowing);
            PawnDiaryRimTestScope.Require(batch != null,
                "The exact bestowing coordinator did not open for an eligible target.");
            pawn.royalty.SetTitle(faction, title, false, false, false);
            AddFirstPsylink(pawn);
            scope.RequireNoNewEvent(() =>
                scope.Component.CompleteRoyalMutationCause(batch, pawn, faction));

            RitualFanoutSignal signal = RitualFanoutSignal.CreateRoyalBestowing(
                bestower, pawn, new List<Pawn>(), 1f);
            PawnDiaryRimTestScope.Require(signal != null,
                "The loaded bestowing facts did not create the canonical ritual fanout.");
            HashSet<string> before = SnapshotEventIds();
            scope.Component.Dispatch(signal);
            List<DiaryEvent> emitted = NewEventsSince(before);
            PawnDiaryRimTestScope.Require(emitted.Count == 1,
                "Bestowing should create one eligible target page, got " + emitted.Count + ".");
            DiaryEvent ritual = emitted[0];
            PawnDiaryRimTestScope.Require(ritual.interactionDefName == "BestowingCeremony",
                "Bestowing emitted a non-canonical ritual Def name.");
            RequireContext(ritual, "royal_cause=" + RoyalMutationCauseTokens.ImperialBestowing);
            RequireContext(ritual, "title_def=" + title.defName);
            RequireContext(ritual, "psylink_cause=" + RoyalMutationCauseTokens.ImperialBestowing);
            RequireNoProgressionEvents(emitted);
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
        }

        /// <summary>Anima linking's psylink change is claimed by its enriched ritual exactly once.</summary>
        [Test]
        public static void AnimaLinkingClaimsPsylinkAsOneEnrichedRitual()
        {
            if (!RequireRoyaltyOrSkip(nameof(AnimaLinkingClaimsPsylinkAsOneEnrichedRitual))) return;
            RoyalMutationBatchSnapshot batch = scope.Component.BeginRoyalMutationCause(
                pawn, null, RoyalMutationCauseTokens.AnimaLinking);
            PawnDiaryRimTestScope.Require(batch != null,
                "The exact anima coordinator did not open for an eligible target.");
            AddFirstPsylink(pawn);
            scope.RequireNoNewEvent(() =>
                scope.Component.CompleteRoyalMutationCause(batch, pawn, null));

            RitualFanoutSignal signal = RitualFanoutSignal.CreateTestFixture(
                pawn, null, new List<Pawn>(), new List<Pawn>(),
                "AnimaTreeLinking", "Anima tree linking", "CompPsylinkable", 1f,
                "fixture anima linking");
            HashSet<string> before = SnapshotEventIds();
            scope.Component.Dispatch(signal);
            List<DiaryEvent> emitted = NewEventsSince(before);
            PawnDiaryRimTestScope.Require(emitted.Count == 1,
                "Anima linking should create one eligible ritual page, got " + emitted.Count + ".");
            RequireContext(emitted[0], "psylink_cause=" + RoyalMutationCauseTokens.AnimaLinking);
            RequireNoProgressionEvents(emitted);
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
        }

        /// <summary>A neuroformer owns one immediate, source-aware PsylinkLevel progression page.</summary>
        [Test]
        public static void NeuroformerCreatesOneCauseAwarePsylinkProgression()
        {
            if (!RequireRoyaltyOrSkip(nameof(NeuroformerCreatesOneCauseAwarePsylinkProgression))) return;
            DiaryEvent progression = scope.FireAndRequireEvent(() =>
            {
                RoyalMutationBatchSnapshot batch = scope.Component.BeginRoyalMutationCause(
                    pawn, null, RoyalMutationCauseTokens.Neuroformer);
                PawnDiaryRimTestScope.Require(batch != null,
                    "The exact neuroformer coordinator did not open for an eligible user.");
                AddFirstPsylink(pawn);
                scope.Component.CompleteRoyalMutationCause(batch, pawn, null);
            }, ProgressionEventData.PsylinkLevelDefName, pawn, null);
            RequireContext(progression, "psylink_cause=" + RoyalMutationCauseTokens.Neuroformer);
            RequireContext(progression, "previous_psylink_level=0");
            RequireContext(progression, "psylink_level=1");
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
        }

        /// <summary>An unmatched exact royal-title memory is delayed, then released unchanged.</summary>
        [Test]
        public static void UnmatchedRoyalTitleMemoryReleasesToOrdinaryThoughtPipeline()
        {
            if (!RequireRoyaltyOrSkip(nameof(UnmatchedRoyalTitleMemoryReleasesToOrdinaryThoughtPipeline))) return;
            RoyalTitleDef title = DefDatabase<RoyalTitleDef>.AllDefsListForReading.FirstOrDefault(row =>
                row?.awardThought != null
                && typeof(Thought_MemoryRoyalTitle).IsAssignableFrom(row.awardThought.thoughtClass));
            PawnDiaryRimTestScope.Require(title?.awardThought != null,
                "Royalty loaded no award Thought_MemoryRoyalTitle fixture.");
            Thought_MemoryRoyalTitle memory = ThoughtMaker.MakeThought(title.awardThought)
                as Thought_MemoryRoyalTitle;
            PawnDiaryRimTestScope.Require(memory != null,
                "The royal-title award thought did not construct Thought_MemoryRoyalTitle.");
            memory.titleDef = title;
            memory.pawn = pawn;
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyThought(memory.def);
            PawnDiaryRimTestScope.Require(group != null,
                "The exact royal-title thought did not classify into the ordinary thought pipeline.");
            PawnDiaryMod.Settings.SetGroupEnabled(group.defName, true);
            DiarySignalPolicyDef thoughtPolicy = DiarySignalPolicies.ForKey(DiarySignalPolicies.Thought);
            float originalThreshold = thoughtPolicy.minMoodOffset;
            thoughtPolicy.minMoodOffset = 0f;
            scope.RegisterCleanup(() => thoughtPolicy.minMoodOffset = originalThreshold);

            int now = Find.TickManager?.TicksGame ?? 0;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            bool staged = RoyalTitleThoughtCorrelation.TryStage(
                new RoyalTitleThoughtSnapshot
                {
                    pawnId = pawn.GetUniqueLoadID(),
                    titleDefName = title.defName,
                    relationshipToken = RoyalTitleThoughtRelationshipTokens.Award,
                    tick = now
                },
                new ThoughtSignal(pawn, memory),
                now,
                policy.titleThoughtCorrelationTicks,
                policy.maximumPendingTitleThoughts);
            PawnDiaryRimTestScope.Require(staged && RoyalTitleThoughtCorrelation.PendingCountForTests == 1,
                "The unmatched title memory was not staged for its richer-owner window.");
            DiaryEvent released = scope.FireAndRequireEvent(
                () => RoyalTitleThoughtCorrelation.Maintain(
                    now + policy.titleThoughtCorrelationTicks + 1,
                    policy.titleThoughtCorrelationTicks),
                title.awardThought.defName,
                pawn,
                null);
            RequireContext(released, "thought=" + title.awardThought.defName);
            PawnDiaryRimTestScope.Require(RoyalTitleThoughtCorrelation.PendingCountForTests == 0,
                "The released title memory remained pending.");
        }

        private static void ForceSignalEnabled(string key)
        {
            DiarySignalPolicyDef policy = DiarySignalPolicies.ForKey(key);
            bool original = policy.enabled;
            policy.enabled = true;
            scope.RegisterCleanup(() => policy.enabled = original);
        }

        private static Faction RequireEmpire()
        {
            PawnDiaryRimTestScope.Require(Faction.OfEmpire != null,
                "Royalty is active but the Empire faction is unavailable in the loaded game.");
            return Faction.OfEmpire;
        }

        private static void RequireTitlePair(
            Faction faction,
            out RoyalTitleDef lower,
            out RoyalTitleDef higher)
        {
            List<RoyalTitleDef> titles = (faction?.def?.RoyalTitlesAwardableInSeniorityOrderForReading
                    ?? new List<RoyalTitleDef>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.defName))
                .OrderBy(row => row.seniority)
                .ToList();
            lower = null;
            higher = null;
            for (int i = 0; i < titles.Count && higher == null; i++)
            {
                for (int j = i + 1; j < titles.Count; j++)
                {
                    if (titles[j].seniority <= titles[i].seniority) continue;
                    lower = titles[i];
                    higher = titles[j];
                    break;
                }
            }
            PawnDiaryRimTestScope.Require(lower != null && higher != null,
                "Royalty loaded no two title Defs with increasing seniority.");
        }

        private static RoyalTitle AddRoyalTitleDirectly(Pawn owner, Faction faction, RoyalTitleDef def)
        {
            PawnDiaryRimTestScope.Require(owner?.royalty != null && faction != null && def != null,
                "A direct title fixture requires a royalty tracker, faction, and title Def.");
            RoyalTitle row = new RoyalTitle
            {
                def = def,
                faction = faction,
                pawn = owner,
                receivedTick = Find.TickManager?.TicksGame ?? 0
            };
            owner.royalty.AllTitlesForReading.Add(row);
            return row;
        }

        private static void RegisterRoyalCleanup(Pawn owner, Faction faction)
        {
            scope.RegisterCleanup(() =>
            {
                if (owner?.royalty?.AllTitlesForReading == null || faction == null) return;
                owner.royalty.AllTitlesForReading.RemoveAll(row =>
                    row != null && ReferenceEquals(row.faction, faction));
            });
        }

        private static void AddFirstPsylink(Pawn owner)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("PsychicAmplifier");
            PawnDiaryRimTestScope.Require(def != null && DlcContext.CurrentPsylinkLevel(owner) == 0,
                "The psylink fixture requires a loaded PsychicAmplifier Def and an unlinked pawn.");
            Hediff hediff = owner.health.AddHediff(def);
            PawnDiaryRimTestScope.Require(hediff != null && DlcContext.CurrentPsylinkLevel(owner) == 1,
                "Adding the real PsychicAmplifier hediff did not create psylink level 1.");
            scope.RegisterCleanup(() =>
            {
                if (owner?.health?.hediffSet?.hediffs?.Contains(hediff) == true)
                    owner.health.RemoveHediff(hediff);
            });
        }

        private static void RequireContext(DiaryEvent diaryEvent, string fragment)
        {
            PawnDiaryRimTestScope.Require(diaryEvent?.gameContext != null
                    && diaryEvent.gameContext.IndexOf(fragment, StringComparison.Ordinal) >= 0,
                "Expected context fragment '" + fragment + "', got '"
                    + (diaryEvent?.gameContext ?? "<null>") + "'.");
        }

        private static void RequireNoProgressionEvents(IEnumerable<DiaryEvent> emitted)
        {
            HashSet<string> progression = new HashSet<string>(StringComparer.Ordinal)
            {
                ProgressionEventData.PsylinkLevelDefName,
                ProgressionEventData.RoyalTitleGainedDefName,
                ProgressionEventData.RoyalTitlePromotedDefName,
                ProgressionEventData.RoyalTitleDemotedDefName,
                ProgressionEventData.RoyalTitleLostDefName
            };
            PawnDiaryRimTestScope.Require(!emitted.Any(row => progression.Contains(row.interactionDefName)),
                "A richer ritual action also emitted a duplicate title/psylink progression page.");
        }

        private static HashSet<string> SnapshotEventIds()
        {
            return new HashSet<string>(EventRepository().AllEvents
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.eventId))
                .Select(row => row.eventId), StringComparer.Ordinal);
        }

        private static List<DiaryEvent> NewEventsSince(HashSet<string> before)
        {
            return EventRepository().AllEvents
                .Where(row => row != null && !before.Contains(row.eventId))
                .ToList();
        }

        private static DiaryEventRepository EventRepository()
        {
            DiaryEventRepository repository = EventsField?.GetValue(scope.Component)
                as DiaryEventRepository;
            PawnDiaryRimTestScope.Require(repository != null,
                "Could not read the event repository for Royalty Phase-4 assertions.");
            return repository;
        }

        private static bool RequireRoyaltyOrSkip(string fixtureName)
        {
            if (ModsConfig.RoyaltyActive) return true;
            PawnDiaryRimTestScope.Require(DlcContext.CaptureRoyalTitles(pawn).Count == 0
                    && DlcContext.CurrentPsylinkLevel(pawn) == 0
                    && RoyalMutationCorrelation.ActiveCountForTests == 0
                    && RoyalMutationCorrelation.PendingCountForTests == 0
                    && RoyalTitleThoughtCorrelation.PendingCountForTests == 0,
                "The no-Royalty path exposed live or pending Royalty state.");
            Log.Message("[Pawn Diary RimTest] SKIP " + fixtureName
                + ": Royalty is not active; guarded path stayed silent and empty.");
            return false;
        }
    }
}

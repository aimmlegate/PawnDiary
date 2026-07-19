// Loaded-game acceptance for Royalty Phase 4 title/psylink correctness and Phase 5 succession.
// These fixtures mutate only disposable pawns and drive the exact hook coordinator, faction-aware
// fallback scanner, ritual ownership bridge, neuroformer owner, delayed title-thought release, and
// succession/heir-appointment paths through production code.
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
        private static readonly FieldInfo PendingSuccessionsField =
            typeof(DiaryGameComponent).GetField("royaltyPendingSuccessions", PrivateInstance);

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
            scope.RequireNoNewEvent(() =>
                pawn.royalty.SetTitle(faction, lower, false, false, false));
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
            PawnDiaryRimTestScope.Require(promotion.tick == loss.tick,
                "The same-tick promotion/loss fixture did not preserve distinct transition events.");
            string lostTitle = DiaryLineCleaner.CleanLine(higher.LabelCap.Resolve());
            PawnDiaryRimTestScope.Require(loss.interactionLabel != null
                    && loss.interactionLabel.IndexOf(lostTitle, StringComparison.OrdinalIgnoreCase) >= 0,
                "The loss header named the absent 'none' title instead of the lost title '"
                    + lostTitle + "'.");
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
        }

        /// <summary>
        /// Vanilla Acolyte inheritance first gives a titleless heir the instant Freeholder rank. That
        /// compatible intermediate callback must be owned by succession, not emitted as a second page.
        /// </summary>
        [Test]
        public static void TitlelessInheritanceOwnsInstantIntermediateTitle()
        {
            if (!RequireRoyaltyOrSkip(nameof(TitlelessInheritanceOwnsInstantIntermediateTitle))) return;
            Faction faction = RequireEmpire();
            RoyalTitleDef inherited = RequireInheritableTitle(faction);
            Pawn deceased = scope.CreateAdultColonist();
            RegisterRoyalCleanup(deceased, faction);
            RegisterRoyalCleanup(pawn, faction);

            // Succession stores detached heir identity and deliberately resolves it back through the
            // production live-colonist roster before writing a page. Generated harness pawns are otherwise
            // unspawned, so make the heir discoverable exactly as it would be in a real colony.
            scope.SpawnAsLiveColonist(pawn);

            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", false);
            deceased.royalty.SetTitle(faction, inherited, false, false, false);
            deceased.royalty.SetHeir(pawn, faction);
            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", true);

            DiaryEvent succession = scope.FireAndRequireEvent(
                () => deceased.royalty.Notify_PawnKilled(),
                ProgressionEventData.RoyalSuccessionDefName,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(succession, pawn);
            RoyalTitleDef intermediate = pawn.royalty.GetCurrentTitle(faction);
            PawnDiaryRimTestScope.Require(intermediate != null
                    && intermediate.seniority < inherited.seniority,
                "Vanilla titleless inheritance did not expose the expected instant intermediate rank.");
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
        }

        /// <summary>
        /// Drives vanilla Notify_PawnKilled with a real inheritable Empire title. The committed edge
        /// must become one heir-POV succession page, and a surrounding bestowing owner cannot restate
        /// the title mutation as Progression or leave a pending ritual fallback.
        /// </summary>
        [Test]
        public static void RealInheritanceCommitOwnsTitleAndBestowingDuplicates()
        {
            if (!RequireRoyaltyOrSkip(nameof(RealInheritanceCommitOwnsTitleAndBestowingDuplicates))) return;
            Faction faction = RequireEmpire();
            RoyalTitleDef inherited = RequireInheritableTitle(faction);
            Pawn deceased = scope.CreateAdultColonist();
            RegisterRoyalCleanup(deceased, faction);
            RegisterRoyalCleanup(pawn, faction);

            // The committed succession path must find the detached heir through the live-pawn roster.
            // Spawning only the heir keeps this fixture faithful without involving the deceased test pawn.
            scope.SpawnAsLiveColonist(pawn);

            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", false);
            deceased.royalty.SetTitle(faction, inherited, false, false, false);
            deceased.royalty.SetHeir(pawn, faction);
            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", true);

            RoyalMutationBatchSnapshot bestowing = scope.Component.BeginRoyalMutationCause(
                pawn, faction, RoyalMutationCauseTokens.ImperialBestowing);
            PawnDiaryRimTestScope.Require(bestowing != null,
                "The succession/bestowing duplicate fixture could not open its mutation boundary.");
            DiaryEvent succession = scope.FireAndRequireEvent(
                () => deceased.royalty.Notify_PawnKilled(),
                ProgressionEventData.RoyalSuccessionDefName,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(succession, pawn);
            RequireContext(succession, "succession_deceased=");
            RequireContext(succession, "succession_heir=");
            RequireContext(succession, "succession_title=");
            RequireContext(succession, "succession_faction=");
            PawnDiaryRimTestScope.Require(
                succession.gameContext.IndexOf("correlation", StringComparison.Ordinal) < 0
                    && succession.gameContext.IndexOf("wasInherited", StringComparison.Ordinal) < 0,
                "Internal succession proof metadata leaked into the prompt context.");

            scope.RequireNoNewEvent(() =>
                scope.Component.CompleteRoyalMutationCause(bestowing, pawn, faction));
            PawnDiaryRimTestScope.Require(RoyalMutationCorrelation.PendingCountForTests == 0,
                "A bestowing batch retained the succession-owned title as a duplicate fallback.");
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
        }

        /// <summary>
        /// A saved succession claim survives beyond the old one-hour deadline, owns the eventual
        /// bestowing title exactly once, then retires so a later independent promotion stays ordinary.
        /// </summary>
        [Test]
        public static void PendingSuccessionClaimsDelayedTargetThenRetires()
        {
            if (!RequireRoyaltyOrSkip(nameof(PendingSuccessionClaimsDelayedTargetThenRetires))) return;
            PawnDiaryRimTestScope.Require(PendingSuccessionsField != null,
                "Could not resolve the component's pending-succession ledger.");
            Faction faction = RequireEmpire();
            RoyalTitleDef inherited = RequireInheritableTitle(faction);
            RoyalTitleDef intermediate = (faction.def.RoyalTitlesAwardableInSeniorityOrderForReading
                    ?? new List<RoyalTitleDef>())
                .Where(row => row != null && row.seniority < inherited.seniority)
                .OrderByDescending(row => row.seniority)
                .FirstOrDefault();
            PawnDiaryRimTestScope.Require(intermediate != null,
                "The delayed succession fixture needs a real predecessor title.");
            RegisterRoyalCleanup(pawn, faction);

            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", false);
            pawn.royalty.SetTitle(faction, intermediate, false, false, false);
            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", true);

            List<RoyalSuccessionState> original =
                PendingSuccessionsField.GetValue(scope.Component) as List<RoyalSuccessionState>;
            scope.RegisterCleanup(() => PendingSuccessionsField.SetValue(scope.Component, original));
            int now = Find.TickManager?.TicksGame ?? 0;
            int commitTick = Math.Max(0, now - 300000);
            PendingSuccessionsField.SetValue(scope.Component, new List<RoyalSuccessionState>
            {
                new RoyalSuccessionState
                {
                    correlationId = "succession|delayed|edge|0",
                    deceasedPawnId = "Pawn_DelayedFormerHolder",
                    deceasedPawnName = "Former holder",
                    heirPawnId = pawn.GetUniqueLoadID(),
                    heirPawnName = DiaryLineCleaner.CleanLine(pawn.LabelShortCap),
                    factionId = faction.GetUniqueLoadID(),
                    factionName = DiaryLineCleaner.CleanLine(faction.Name),
                    inheritedTitleDefName = inherited.defName,
                    inheritedTitleLabel = DiaryLineCleaner.CleanLine(inherited.LabelCap.Resolve()),
                    inheritedTitleSeniority = inherited.seniority,
                    previousHeirTitleDefName = string.Empty,
                    previousHeirTitleLabel = string.Empty,
                    previousHeirTitleSeniority = -1,
                    currentHeirTitleDefName = intermediate.defName,
                    currentHeirTitleLabel = DiaryLineCleaner.CleanLine(intermediate.LabelCap.Resolve()),
                    currentHeirTitleSeniority = intermediate.seniority,
                    candidateTick = commitTick,
                    commitTick = commitTick,
                    // Deliberately model the old additive save row after its former one-hour expiry.
                    expiresTick = commitTick,
                    pageClaimed = true
                }
            });

            RoyalMutationBatchSnapshot bestowing = scope.Component.BeginRoyalMutationCause(
                pawn, faction, RoyalMutationCauseTokens.ImperialBestowing);
            PawnDiaryRimTestScope.Require(bestowing != null,
                "The delayed succession fixture could not open its bestowing boundary.");
            scope.RequireNoNewEvent(() =>
            {
                pawn.royalty.SetTitle(faction, inherited, false, false, false);
                scope.Component.CompleteRoyalMutationCause(bestowing, pawn, faction);
            });
            PawnDiaryRimTestScope.Require(
                (PendingSuccessionsField.GetValue(scope.Component) as List<RoyalSuccessionState>)?.Count == 0,
                "The exact inherited target did not retire its saved succession fact.");
            PawnDiaryRimTestScope.Require(RoyalMutationCorrelation.PendingCountForTests == 0,
                "The duplicate bestowing adapter retained a succession-owned title fallback.");

            // Reset only transient same-action ownership, then repeat the same title edge as a genuinely
            // later action. With the saved fact terminally removed, ordinary progression must own it.
            RoyalSuccessionCorrelation.Clear();
            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", false);
            pawn.royalty.SetTitle(faction, intermediate, false, false, false);
            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", true);
            scope.FireAndRequireEvent(
                () => pawn.royalty.SetTitle(faction, inherited, false, false, false),
                ProgressionEventData.RoyalTitlePromotedDefName,
                pawn,
                null,
                rejectOtherTestPawnEvents: true);
        }

        /// <summary>
        /// Vanilla's equal-or-higher outcome is not a transfer: no succession page or delayed title
        /// page may appear when the named heir already outranks the deceased holder.
        /// </summary>
        [Test]
        public static void EqualOrHigherHeirDoesNotCreateSuccession()
        {
            if (!RequireRoyaltyOrSkip(nameof(EqualOrHigherHeirDoesNotCreateSuccession))) return;
            Faction faction = RequireEmpire();
            RoyalTitleDef lower;
            RoyalTitleDef higher;
            RequireInheritableTitlePair(faction, out lower, out higher);
            Pawn deceased = scope.CreateAdultColonist();
            RegisterRoyalCleanup(deceased, faction);
            RegisterRoyalCleanup(pawn, faction);

            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", false);
            deceased.royalty.SetTitle(faction, lower, false, false, false);
            pawn.royalty.SetTitle(faction, higher, false, false, false);
            deceased.royalty.SetHeir(pawn, faction);
            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", true);
            scope.RequireNoNewEvent(() => deceased.royalty.Notify_PawnKilled());
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));

            Pawn equalDeceased = scope.CreateAdultColonist();
            RegisterRoyalCleanup(equalDeceased, faction);
            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", false);
            pawn.royalty.SetTitle(faction, lower, false, false, false);
            equalDeceased.royalty.SetTitle(faction, lower, false, false, false);
            equalDeceased.royalty.SetHeir(pawn, faction);
            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", true);
            scope.RequireNoNewEvent(() => equalDeceased.royalty.Notify_PawnKilled());
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
        }

        /// <summary>Direct SetHeir stays silent; the proven ChangeRoyalHeir quest signal emits once.</summary>
        [Test]
        public static void ExplicitChangeRoyalHeirQuestEmitsWhileAutomaticAssignmentStaysSilent()
        {
            if (!RequireRoyaltyOrSkip(
                nameof(ExplicitChangeRoyalHeirQuestEmitsWhileAutomaticAssignmentStaysSilent))) return;
            Faction faction = RequireEmpire();
            RoyalTitleDef title = RequireInheritableTitle(faction);
            Pawn holder = scope.CreateAdultColonist();
            Pawn previous = scope.CreateAdultColonist();
            RegisterRoyalCleanup(holder, faction);

            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", false);
            holder.royalty.SetTitle(faction, title, false, false, false);
            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", true);
            scope.RequireNoNewEvent(() => holder.royalty.SetHeir(previous, faction));

            QuestPart_ChangeHeir questPart = new QuestPart_ChangeHeir
            {
                holder = holder,
                heir = pawn,
                faction = faction,
                inSignal = "PawnDiary_RoyalHeirFixture"
            };
            DiaryEvent appointment = scope.FireAndRequireEvent(
                () => questPart.Notify_QuestSignalReceived(
                    new Signal("PawnDiary_RoyalHeirFixture")),
                ProgressionEventData.RoyalHeirAppointedDefName,
                pawn,
                null);
            scope.RequireSoloRef(appointment, pawn);
            RequireContext(appointment, "succession_heir=");
            RequireContext(appointment, "succession_title=");
            RequireContext(appointment, "succession_faction=");
            PawnDiaryRimTestScope.Require(
                appointment.gameContext.IndexOf("succession_deceased=", StringComparison.Ordinal) < 0,
                "An heir appointment invented a deceased title holder.");
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
            Pawn participant = scope.CreateAdultColonist();
            HashSet<string> before = SnapshotEventIds();

            RoyalMutationBatchSnapshot batch = scope.Component.BeginRoyalMutationCause(
                pawn, faction, RoyalMutationCauseTokens.ImperialBestowing);
            PawnDiaryRimTestScope.Require(batch != null,
                "The exact bestowing coordinator did not open for an eligible target.");
            pawn.royalty.SetTitle(faction, title, false, false, false);
            AddFirstPsylink(pawn);
            scope.RequireNoNewEvent(() =>
                scope.Component.CompleteRoyalMutationCause(batch, pawn, faction));

            RitualFanoutSignal signal = RitualFanoutSignal.CreateRoyalBestowing(
                bestower, pawn, new List<Pawn> { participant }, 1f);
            PawnDiaryRimTestScope.Require(signal != null,
                "The loaded bestowing facts did not create the canonical ritual fanout.");
            PawnDiaryRimTestScope.Require(RoyalMutationCorrelation.PendingCountForTests == 1,
                "The bestowing fixture lost its pending Royalty owner before fanout.");
            signal.NotifyPageCreated(scope.Component, participant.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(RoyalMutationCorrelation.PendingCountForTests == 1,
                "An attendee page claimed the target-only Royalty mutation.");
            scope.Component.Dispatch(signal);
            List<DiaryEvent> emitted = NewEventsSince(before);
            PawnDiaryRimTestScope.Require(emitted.Count == 2,
                "Bestowing should retain target/participant ritual perspectives, got "
                    + emitted.Count + ".");
            DiaryEvent ritual = emitted.FirstOrDefault(row =>
                row.initiatorPawnId == pawn.GetUniqueLoadID());
            DiaryEvent participantRitual = emitted.FirstOrDefault(row =>
                row.initiatorPawnId == participant.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(ritual != null && participantRitual != null,
                "Bestowing did not preserve distinct target and participant perspectives.");
            PawnDiaryRimTestScope.Require(ritual.interactionDefName == "BestowingCeremony",
                "Bestowing emitted a non-canonical ritual Def name.");
            RequireContext(ritual, "royal_cause=" + RoyalMutationCauseTokens.ImperialBestowing);
            RequireContext(ritual, "title_def=" + title.defName);
            RequireContext(ritual, "psylink_cause=" + RoyalMutationCauseTokens.ImperialBestowing);
            PawnDiaryRimTestScope.Require(participantRitual.gameContext == null
                    || participantRitual.gameContext.IndexOf("royal_cause=", StringComparison.Ordinal) < 0,
                "An attendee page incorrectly claimed the target's Royalty mutation context.");
            RequireNoProgressionEvents(emitted);
            PawnDiaryRimTestScope.Require(RoyalTitleThoughtCorrelation.PendingCountForTests == 0,
                "The enriched bestowing owner left a duplicate title memory pending.");
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
        }

        /// <summary>
        /// Disabling the canonical Royal ritual group consumes an exact bestowing action silently.
        /// The Royalty policy master also suppresses the canonical bestowing/anima pages themselves.
        /// Neither path may leave a pending batch, delayed title thought, or later scanner page.
        /// </summary>
        [Test]
        public static void DisabledRoyalRitualDoesNotLeakIntoProgressionOrThoughtFallback()
        {
            if (!RequireRoyaltyOrSkip(nameof(DisabledRoyalRitualDoesNotLeakIntoProgressionOrThoughtFallback)))
                return;
            Faction faction = RequireEmpire();
            RoyalTitleDef title;
            RoyalTitleDef ignored;
            RequireTitlePair(faction, out title, out ignored);
            RegisterRoyalCleanup(pawn, faction);
            PawnDiaryMod.Settings.SetGroupEnabled("ritualRoyal", false);

            scope.RequireNoNewEvent(() =>
            {
                RoyalMutationBatchSnapshot batch = scope.Component.BeginRoyalMutationCause(
                    pawn, faction, RoyalMutationCauseTokens.ImperialBestowing);
                PawnDiaryRimTestScope.Require(batch != null,
                    "The disabled-group fixture could not open its exact mutation boundary.");
                pawn.royalty.SetTitle(faction, title, false, false, false);
                AddFirstPsylink(pawn);
                scope.Component.CompleteRoyalMutationCause(batch, pawn, faction);
            });
            PawnDiaryRimTestScope.Require(RoyalMutationCorrelation.PendingCountForTests == 0
                    && RoyalTitleThoughtCorrelation.PendingCountForTests == 0,
                "A disabled Royal ritual left delayed mutation/thought ownership behind.");
            PawnDiaryMod.Settings.SetGroupEnabled("ritualRoyal", true);
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));

            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            bool originalPolicyEnabled = policy.enabled;
            policy.enabled = false;
            try
            {
                scope.RequireNoNewEvent(() =>
                {
                    RitualFanoutSignal bestowing = RitualFanoutSignal.CreateRoyalBestowing(
                        pawn, pawn, new List<Pawn>(), 1f);
                    PawnDiaryRimTestScope.Require(bestowing == null,
                        "The Royalty policy master left the canonical bestowing page enabled.");

                    RitualFanoutSignal anima = RitualFanoutSignal.CreateTestFixture(
                        pawn, null, new List<Pawn>(), new List<Pawn>(),
                        "AnimaTreeLinking", "Anima tree linking", "CompPsylinkable", 1f,
                        "fixture policy-master anima linking");
                    PawnDiaryRimTestScope.Require(string.IsNullOrEmpty(anima?.ColonyDedupKey),
                        "The Royalty policy master left the canonical anima page enabled.");
                    scope.Component.Dispatch(anima);
                });
            }
            finally
            {
                policy.enabled = originalPolicyEnabled;
            }
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

        /// <summary>
        /// When a combined ritual batch expires while title pages are disabled, its enabled psylink
        /// fallback owns the whole action and must claim the exact title memory before that memory's
        /// equally-sized expiry window releases an ordinary Thought page.
        /// </summary>
        [Test]
        public static void CombinedPsylinkFallbackClaimsExpiredTitleMemory()
        {
            if (!RequireRoyaltyOrSkip(nameof(CombinedPsylinkFallbackClaimsExpiredTitleMemory))) return;
            Faction faction = RequireEmpire();
            RoyalTitleDef title = faction.def.RoyalTitlesAwardableInSeniorityOrderForReading
                .FirstOrDefault(row => row?.awardThought != null
                    && typeof(Thought_MemoryRoyalTitle).IsAssignableFrom(row.awardThought.thoughtClass));
            PawnDiaryRimTestScope.Require(title?.awardThought != null,
                "Royalty loaded no combined-fallback title with an award memory.");
            RegisterRoyalCleanup(pawn, faction);
            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", false);
            PawnDiaryMod.Settings.SetGroupEnabled("progressionPsylink", true);
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));

            RoyalMutationBatchSnapshot batch = scope.Component.BeginRoyalMutationCause(
                pawn, faction, RoyalMutationCauseTokens.ImperialBestowing);
            PawnDiaryRimTestScope.Require(batch != null,
                "The combined fallback fixture could not open its exact mutation boundary.");
            AddRoyalTitleDirectly(pawn, faction, title);
            AddFirstPsylink(pawn);

            Thought_MemoryRoyalTitle memory = ThoughtMaker.MakeThought(title.awardThought)
                as Thought_MemoryRoyalTitle;
            PawnDiaryRimTestScope.Require(memory != null,
                "The combined fallback title memory did not construct.");
            memory.titleDef = title;
            memory.pawn = pawn;
            DiaryInteractionGroupDef thoughtGroup = InteractionGroups.ClassifyThought(memory.def);
            PawnDiaryRimTestScope.Require(thoughtGroup != null,
                "The combined fallback title memory did not classify as an ordinary Thought.");
            PawnDiaryMod.Settings.SetGroupEnabled(thoughtGroup.defName, true);
            DiarySignalPolicyDef thoughtPolicy = DiarySignalPolicies.ForKey(DiarySignalPolicies.Thought);
            float originalThreshold = thoughtPolicy.minMoodOffset;
            thoughtPolicy.minMoodOffset = 0f;
            scope.RegisterCleanup(() => thoughtPolicy.minMoodOffset = originalThreshold);

            int now = Find.TickManager?.TicksGame ?? 0;
            PawnDiaryRimTestScope.Require(now > 1,
                "The loaded game needs at least two ticks for an expired ownership fixture.");
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            int originalMutationWindow = policy.titleCorrelationTicks;
            int originalThoughtWindow = policy.titleThoughtCorrelationTicks;
            policy.titleCorrelationTicks = 1;
            policy.titleThoughtCorrelationTicks = 1;
            scope.RegisterCleanup(() =>
            {
                policy.titleCorrelationTicks = originalMutationWindow;
                policy.titleThoughtCorrelationTicks = originalThoughtWindow;
            });
            RoyalTitleThoughtSnapshot fact = new RoyalTitleThoughtSnapshot
            {
                pawnId = pawn.GetUniqueLoadID(),
                titleDefName = title.defName,
                relationshipToken = RoyalTitleThoughtRelationshipTokens.Award,
                tick = now
            };
            bool staged = RoyalTitleThoughtCorrelation.TryStage(
                fact,
                new ThoughtSignal(pawn, memory),
                now,
                policy.titleThoughtCorrelationTicks,
                policy.maximumPendingTitleThoughts);
            PawnDiaryRimTestScope.Require(staged,
                "The combined fallback title memory did not enter its exact ownership window.");
            scope.RequireNoNewEvent(() =>
                scope.Component.CompleteRoyalMutationCause(batch, pawn, faction));
            PawnDiaryRimTestScope.Require(batch.titleMutation != null && batch.psylinkMutation != null
                    && RoyalMutationCorrelation.PendingCountForTests == 1,
                "The combined title/psylink mutation did not enter pending ritual ownership.");

            int expiredTick = now - 2;
            batch.openedTick = expiredTick;
            if (batch.scope != null) batch.scope.openedTick = expiredTick;
            batch.titleMutation.tick = expiredTick;
            batch.psylinkMutation.tick = expiredTick;
            fact.tick = expiredTick;
            HashSet<string> before = SnapshotEventIds();
            scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false);
            List<DiaryEvent> emitted = NewEventsSince(before);
            PawnDiaryRimTestScope.Require(emitted.Count == 1
                    && emitted[0].interactionDefName == ProgressionEventData.PsylinkLevelDefName,
                "The expired combined batch should create one psylink page, got "
                    + string.Join(", ", emitted.Select(row => row.interactionDefName).ToArray()) + ".");
            PawnDiaryRimTestScope.Require(RoyalMutationCorrelation.PendingCountForTests == 0
                    && RoyalTitleThoughtCorrelation.PendingCountForTests == 0,
                "The psylink fallback left duplicate mutation/title-memory ownership pending.");
            scope.RequireNoNewEvent(() => RoyalTitleThoughtCorrelation.Maintain(
                now, policy.titleThoughtCorrelationTicks));
        }

        /// <summary>The real neuroformer Comp hook owns one immediate source-aware Psylink page.</summary>
        [Test]
        public static void RealNeuroformerHookCreatesOneCauseAwarePsylinkProgression()
        {
            if (!RequireRoyaltyOrSkip(nameof(RealNeuroformerHookCreatesOneCauseAwarePsylinkProgression)))
                return;
            PawnDiaryRimTestScope.Require(DiaryRoyaltyPatches.NeuroformerHookReady,
                "The neuroformer Harmony seam was not registered in the loaded game.");
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("PsychicAmplifier");
            Thing neuroformer = def == null ? null : ThingMaker.MakeThing(def);
            CompUseEffect_InstallImplant comp = neuroformer?.TryGetComp<CompUseEffect_InstallImplant>();
            PawnDiaryRimTestScope.Require(comp != null,
                "The real PsychicAmplifier item did not expose CompUseEffect_InstallImplant.");
            List<Hediff> previousHediffs = new List<Hediff>(pawn.health.hediffSet.hediffs);
            scope.RegisterCleanup(() =>
            {
                if (pawn?.health?.hediffSet?.hediffs == null) return;
                List<Hediff> added = pawn.health.hediffSet.hediffs
                    .Where(row => row != null && !previousHediffs.Contains(row)).ToList();
                for (int i = 0; i < added.Count; i++) pawn.health.RemoveHediff(added[i]);
            });

            DiaryEvent progression = scope.FireAndRequireEvent(
                () => comp.DoEffect(pawn),
                ProgressionEventData.PsylinkLevelDefName, pawn, null);
            RequireContext(progression, "psylink_cause=" + RoyalMutationCauseTokens.Neuroformer);
            RequireContext(progression, "previous_psylink_level=0");
            RequireContext(progression, "psylink_level=1");
            PawnDiaryRimTestScope.Require(DlcContext.CurrentPsylinkLevel(pawn) == 1,
                "The real neuroformer call did not install psylink level 1.");
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
        }

        /// <summary>A title owner arriving before its memory still suppresses that exact memory.</summary>
        [Test]
        public static void ReverseOrderTitleOwnerSuppressesExactLaterMemory()
        {
            if (!RequireRoyaltyOrSkip(nameof(ReverseOrderTitleOwnerSuppressesExactLaterMemory))) return;
            RoyalTitleDef title = DefDatabase<RoyalTitleDef>.AllDefsListForReading.FirstOrDefault(row =>
                row?.awardThought != null
                && typeof(Thought_MemoryRoyalTitle).IsAssignableFrom(row.awardThought.thoughtClass));
            PawnDiaryRimTestScope.Require(title?.awardThought != null,
                "Royalty loaded no award Thought_MemoryRoyalTitle fixture.");
            Thought_MemoryRoyalTitle memory = ThoughtMaker.MakeThought(title.awardThought)
                as Thought_MemoryRoyalTitle;
            PawnDiaryRimTestScope.Require(memory != null,
                "The reverse-order title memory did not construct.");
            memory.titleDef = title;
            memory.pawn = pawn;
            int now = Find.TickManager?.TicksGame ?? 0;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            RoyalTitleThoughtCorrelation.Claim(
                pawn.GetUniqueLoadID(), string.Empty, title.defName,
                now, policy.titleThoughtCorrelationTicks);
            bool suppressed = RoyalTitleThoughtCorrelation.TryStage(
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
            PawnDiaryRimTestScope.Require(suppressed
                    && RoyalTitleThoughtCorrelation.PendingCountForTests == 0,
                "The recent exact title owner did not suppress the later matching memory.");
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

        /// <summary>
        /// An unmatched title memory is released by the component's real pre-save pass, serialized in
        /// that same Scribe document, and reloadable from the production diaryEvents key.
        /// </summary>
        [Test]
        public static void PendingRoyalTitleMemoryFlushesToOrdinaryThoughtBeforeSave()
        {
            if (!RequireRoyaltyOrSkip(nameof(PendingRoyalTitleMemoryFlushesToOrdinaryThoughtBeforeSave))) return;
            Pawn memoryPawn = scope.CreateAdultColonist();
            RoyalTitleDef title = DefDatabase<RoyalTitleDef>.AllDefsListForReading.FirstOrDefault(row =>
                row?.awardThought != null
                && typeof(Thought_MemoryRoyalTitle).IsAssignableFrom(row.awardThought.thoughtClass));
            PawnDiaryRimTestScope.Require(title?.awardThought != null,
                "Royalty loaded no award Thought_MemoryRoyalTitle save-flush fixture.");
            Thought_MemoryRoyalTitle memory = ThoughtMaker.MakeThought(title.awardThought)
                as Thought_MemoryRoyalTitle;
            PawnDiaryRimTestScope.Require(memory != null,
                "The save-flush royal-title memory did not construct.");
            memory.titleDef = title;
            memory.pawn = memoryPawn;
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyThought(memory.def);
            PawnDiaryRimTestScope.Require(group != null,
                "The save-flush title memory did not classify into ordinary Thought capture.");
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
                    pawnId = memoryPawn.GetUniqueLoadID(),
                    titleDefName = title.defName,
                    relationshipToken = RoyalTitleThoughtRelationshipTokens.Award,
                    tick = now
                },
                new ThoughtSignal(memoryPawn, memory),
                now,
                policy.titleThoughtCorrelationTicks,
                policy.maximumPendingTitleThoughts);
            PawnDiaryRimTestScope.Require(staged,
                "The title memory did not enter the pre-save ownership window.");

            DiaryEventRepository reloadedEvents = null;
            DiaryEvent released = scope.FireAndRequireEvent(
                () => reloadedEvents = SaveComponentAndReloadEvents(),
                title.awardThought.defName,
                memoryPawn,
                null);
            RequireContext(released, "thought=" + title.awardThought.defName);
            PawnDiaryRimTestScope.Require(RoyalTitleThoughtCorrelation.PendingCountForTests == 0,
                "The pre-save title-memory flush left a pending row.");
            DiaryEvent reloaded = reloadedEvents?.FindEvent(released.eventId);
            PawnDiaryRimTestScope.Require(reloaded != null
                    && reloaded.interactionDefName == title.awardThought.defName
                    && reloaded.initiatorPawnId == memoryPawn.GetUniqueLoadID(),
                "The pre-save title-memory page was not serialized/reloaded from diaryEvents.");
        }

        /// <summary>
        /// A direct/modded title mutation can leave its exact award memory pending until the scanner
        /// runs. Saving in that window must reconcile the rich title page before flushing unmatched
        /// memories, and continuing after the save must not add either page again.
        /// </summary>
        [Test]
        public static void ScannerTitleMemoryReconcilesBeforeSaveWithoutDuplicate()
        {
            if (!RequireRoyaltyOrSkip(nameof(ScannerTitleMemoryReconcilesBeforeSaveWithoutDuplicate))) return;
            Faction faction = RequireEmpire();
            RoyalTitleDef title = faction.def.RoyalTitlesAwardableInSeniorityOrderForReading
                .FirstOrDefault(row => row?.awardThought != null
                    && typeof(Thought_MemoryRoyalTitle).IsAssignableFrom(row.awardThought.thoughtClass));
            PawnDiaryRimTestScope.Require(title?.awardThought != null,
                "Royalty loaded no scanner/save title with an award memory.");
            RegisterRoyalCleanup(pawn, faction);

            // The production pre-save reconciliation scans live map/caravan colonists rather than arbitrary
            // generated objects. Put this fixture pawn on the map so that narrow save seam sees it.
            scope.SpawnAsLiveColonist(pawn);
            PawnDiaryMod.Settings.SetGroupEnabled("progressionRoyalTitle", true);
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));

            AddRoyalTitleDirectly(pawn, faction, title);
            Thought_MemoryRoyalTitle memory = ThoughtMaker.MakeThought(title.awardThought)
                as Thought_MemoryRoyalTitle;
            PawnDiaryRimTestScope.Require(memory != null,
                "The scanner/save royal-title memory did not construct.");
            memory.titleDef = title;
            memory.pawn = pawn;
            DiaryInteractionGroupDef thoughtGroup = InteractionGroups.ClassifyThought(memory.def);
            PawnDiaryRimTestScope.Require(thoughtGroup != null,
                "The scanner/save title memory did not classify into ordinary Thought capture.");
            PawnDiaryMod.Settings.SetGroupEnabled(thoughtGroup.defName, true);
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
            PawnDiaryRimTestScope.Require(staged,
                "The scanner/save title memory did not enter its exact ownership window.");

            HashSet<string> before = SnapshotEventIds();
            DiaryEventRepository reloadedEvents = SaveComponentAndReloadEvents();
            List<DiaryEvent> emitted = NewEventsSince(before);
            PawnDiaryRimTestScope.Require(emitted.Count == 1,
                "Pre-save title reconciliation should create exactly one page, got "
                    + emitted.Count + ".");
            DiaryEvent gained = emitted[0];
            PawnDiaryRimTestScope.Require(
                gained.interactionDefName == ProgressionEventData.RoyalTitleGainedDefName,
                "Pre-save reconciliation released an ordinary Thought instead of the rich title page.");
            PawnDiaryRimTestScope.Require(
                reloadedEvents?.FindEvent(gained.eventId)?.interactionDefName
                    == ProgressionEventData.RoyalTitleGainedDefName,
                "The reconciled title page was not serialized in the same save.");
            PawnDiaryRimTestScope.Require(RoyalTitleThoughtCorrelation.PendingCountForTests == 0,
                "The reconciled title page left its exact award memory pending.");
            scope.RequireNoNewEvent(() =>
                scope.Component.ScanPawnProgressionForDiaryEvents(pawn, true, false));
        }

        private static DiaryEventRepository SaveComponentAndReloadEvents()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_royalty_presave_" + Guid.NewGuid().ToString("N") + ".xml");
            DiaryEventRepository loaded = new DiaryEventRepository();
            try
            {
                Scribe.saver.InitSaving(path, "root");
                // Exercise the exact narrow pre-save component seam used by ExposeData, then serialize
                // the production repository key in the same Scribe document. Calling the whole live
                // component ExposeData here would flush unrelated developer-save transient queues.
                scope.Component.FlushRoyalTitleThoughtsBeforeSave();
                EventRepository().ExposeEvents("diaryEvents");
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                loaded.ExposeEvents("diaryEvents");
                Scribe.loader.FinalizeLoading();
                loaded.RebuildIndex();
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                try
                {
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                }
                catch
                {
                    // Temp cleanup is best-effort; Scribe/event assertions remain authoritative.
                }
            }
            return loaded;
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

        private static RoyalTitleDef RequireInheritableTitle(Faction faction)
        {
            RoyalTitleDef title = (faction?.def?.RoyalTitlesAwardableInSeniorityOrderForReading
                    ?? new List<RoyalTitleDef>())
                .FirstOrDefault(row => row?.canBeInherited == true
                    && row.GetInheritanceWorker(faction) != null);
            PawnDiaryRimTestScope.Require(title != null,
                "Royalty loaded no Empire title with a configured inheritance worker.");
            return title;
        }

        private static void RequireInheritableTitlePair(
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
                if (!titles[i].canBeInherited || titles[i].GetInheritanceWorker(faction) == null) continue;
                higher = titles.FirstOrDefault(row => row.seniority >= titles[i].seniority
                    && !ReferenceEquals(row, titles[i]));
                if (higher != null) lower = titles[i];
            }
            PawnDiaryRimTestScope.Require(lower != null && higher != null,
                "Royalty loaded no inheritable Empire title with an equal-or-higher heir fixture rank.");
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

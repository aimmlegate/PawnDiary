// Loaded-game acceptance for Royalty Phase 6 permits and Phase 8 compatibility. These fixtures use
// real or synthetic RoyalTitlePermitDefs, real tracker GetPermit calls, and FactionPermit.Notify_Used;
// no UI selection or targeting intent is treated as success. Quick-aid fallback dispatch is observed
// through the production correlation owner's test seam so a developer's real colonists never receive
// test raid pages.
using System;
using System.Collections.Generic;
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
    /// <summary>Proves exact permit success, silence, dedup, raid ownership, expiry, and reset.</summary>
    [TestSuite]
    public static class PawnDiaryRoyaltyPermitFlowTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;
        private static RoyaltyPolicySnapshot livePolicy;
        private static bool originalPolicyEnabled;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("royalPermitDramatic", "raidFriendly");
            RoyaltyTransientState.Reset();
            livePolicy = DiaryRoyaltyPolicy.Snapshot();
            originalPolicyEnabled = livePolicy.enabled;
            livePolicy.enabled = true;
            pawn = scope.CreateAdultColonist();
        }

        [AfterEach]
        public static void TearDown()
        {
            try { scope?.TearDown(); }
            finally
            {
                if (livePolicy != null) livePolicy.enabled = originalPolicyEnabled;
                RoyaltyTransientState.Reset();
                RaidExecutePatch.SetRaidSubmitOverrideForTests(null);
                scope = null;
                pawn = null;
                livePolicy = null;
            }
        }

        /// <summary>Audits the exact owner, success, and raid target signatures and Pawn Diary patches.</summary>
        [Test]
        public static void ExactPermitHarmonyTargetsAreRegistered()
        {
            if (!RequireRoyaltyOrSkip(nameof(ExactPermitHarmonyTargetsAreRegistered))) return;
            PawnDiaryRimTestScope.Require(
                DiaryRoyaltyPatches.PermitOwnerHookReady && DiaryRoyaltyPatches.PermitUseHookReady,
                "Pawn Diary reported an incomplete Royalty permit hook set.");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.GetPermit),
                    new[] { typeof(RoyalTitlePermitDef), typeof(Faction) }),
                null,
                "PermitGetPostfix");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(typeof(FactionPermit), nameof(FactionPermit.Notify_Used),
                    Type.EmptyTypes),
                "PermitUsedPrefix",
                "PermitUsedPostfix");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute),
                    new[] { typeof(IncidentParms) }),
                null,
                "Postfix",
                typeof(RaidExecutePatch));
        }

        /// <summary>Each reviewed family emits from the real vanilla success callback with exact facts.</summary>
        [Test]
        public static void RealSuccessfulUsesEmitEveryDramaticFamily()
        {
            if (!RequireRoyaltyOrSkip(nameof(RealSuccessfulUsesEmitEveryDramaticFamily))) return;
            Faction faction = RequireEmpire();
            RoyalTitlePermitDef[] defs =
            {
                RequirePermit("CallMilitaryAidSmall"),
                RequirePermit("CallTransportShuttle"),
                RequirePermit("CallOrbitalStrike"),
                RequirePermit("CallOrbitalSalvo")
            };
            scope.SpawnAsLiveColonist(pawn);
            AddRoyalTitleFixture(faction, HighestRequiredTitle(defs));
            string[] expectedEvents =
            {
                RoyalPermitPolicy.MilitaryAidEventDefName,
                RoyalPermitPolicy.TransportShuttleEventDefName,
                RoyalPermitPolicy.OrbitalStrikeEventDefName,
                RoyalPermitPolicy.OrbitalSalvoEventDefName
            };
            for (int i = 0; i < defs.Length; i++)
            {
                FactionPermit permit = AddPermitFixture(defs[i], faction);
                PawnDiaryRimTestScope.Require(
                    ReferenceEquals(pawn.royalty.GetPermit(defs[i], faction), permit),
                    "The real tracker did not return the exact permit instance for " + defs[i].defName + ".");
                DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                    () => permit.Notify_Used(), expectedEvents[i], pawn, null, true);
                scope.RequireSoloRef(diaryEvent, pawn);
                RequireContext(diaryEvent, "royal_permit=" + RoyalPermitPolicy.FamilyFor(
                    defs[i].defName, DiaryRoyaltyPolicy.Snapshot()));
                RequireContext(diaryEvent, "permit_def=" + defs[i].defName);
                RequireContext(diaryEvent, "permit_faction=" + DiaryLineCleaner.CleanLine(faction.Name));
                RequireContext(diaryEvent, "permit_title=" + DiaryLineCleaner.CleanLine(
                    permit.Title.GetLabelCapFor(pawn)));
                RequireContext(diaryEvent, "permit_setting=" + DiaryLineCleaner.CleanLine(
                    pawn.MapHeld.Parent.LabelCap));
                string family = RoyalPermitPolicy.FamilyFor(
                    defs[i].defName, DiaryRoyaltyPolicy.Snapshot());
                List<NarrativeEvidence> evidence =
                    diaryEvent.NarrativeEvidenceForRole(DiaryEvent.InitiatorRole);
                PawnDiaryRimTestScope.Require(evidence.Count == 1
                        && evidence[0].facet == NarrativeFacetTokens.IdentityTransition
                        && evidence[0].phase == family
                        && evidence[0].subjectKind == NarrativeSubjectKindTokens.Pawn
                        && evidence[0].subjectId == pawn.GetUniqueLoadID()
                        && evidence[0].sourceDomain == RoyaltyNarrativeEvidenceFactory.PermitSourceDomain
                        && evidence[0].sourceDefName == expectedEvents[i]
                        && string.IsNullOrEmpty(evidence[0].arcKey),
                    "The exact permit page did not freeze its source-owned N3-R authority evidence.");
                string titleKeyPrefix = "royalty|title|" + pawn.GetUniqueLoadID() + "|"
                    + faction.GetUniqueLoadID() + "|";
                PawnDiaryRimTestScope.Require(
                    diaryEvent.NarrativeSelectedCandidateKeysForRole(DiaryEvent.InitiatorRole)
                        .Exists(key => key != null
                            && key.StartsWith(titleKeyPrefix, StringComparison.Ordinal))
                        && !string.IsNullOrWhiteSpace(
                            diaryEvent.NarrativeContextForRole(DiaryEvent.InitiatorRole))
                        && diaryEvent.NarrativeReferencesForRole(DiaryEvent.InitiatorRole)
                            .Exists(reference => reference != null
                                && reference.facet == NarrativeFacetTokens.IdentityTransition
                                && reference.subjectKind == NarrativeSubjectKindTokens.Pawn
                                && reference.subjectId == pawn.GetUniqueLoadID()),
                    "The permit page did not select the existing exact-POV Royalty title provider.");
                PawnDiaryRimTestScope.Require(permit.LastUsedTick == Find.TickManager.TicksGame,
                    "Notify_Used did not commit vanilla lastUsedTick for " + defs[i].defName + ".");
            }
        }

        /// <summary>GetPermit/UI intent and all reviewed routine permits stay silent without success.</summary>
        [Test]
        public static void SelectionIntentAndExcludedPermitsStaySilent()
        {
            if (!RequireRoyaltyOrSkip(nameof(SelectionIntentAndExcludedPermitsStaySilent))) return;
            Faction faction = RequireEmpire();
            RoyalTitlePermitDef dramatic = RequirePermit("CallMilitaryAidLarge");
            AddRoyalTitleFixture(faction, dramatic.minTitle);
            FactionPermit pending = AddPermitFixture(dramatic, faction);
            scope.RequireNoNewEvent(() =>
            {
                // This exact lookup is reached while permit UI/options are being assembled. Merely
                // selecting or cancelling therefore populates ownership but cannot authorize a page.
                FactionPermit selected = pawn.royalty.GetPermit(dramatic, faction);
                PawnDiaryRimTestScope.Require(ReferenceEquals(selected, pending),
                    "The cancelled-at-intent fixture lost its exact permit instance.");
            });

            // Exercise an installed vanilla target validator as a real rejected-use path. A fresh
            // worker avoids disturbing the shared Def worker/Targeter state in the developer's game.
            RoyalTitlePermitDef shuttleDef = RequirePermit("CallTransportShuttle");
            scope.SpawnAsLiveColonist(pawn);
            RoyalTitlePermitWorker_CallShuttle failedWorker =
                new RoyalTitlePermitWorker_CallShuttle { def = shuttleDef };
            AccessTools.Field(typeof(RoyalTitlePermitWorker_Targeted), "caller")
                .SetValue(failedWorker, pawn);
            AccessTools.Field(typeof(RoyalTitlePermitWorker_Targeted), "map")
                .SetValue(failedWorker, Find.CurrentMap);
            bool accepted = true;
            scope.RequireNoNewEvent(() =>
                accepted = failedWorker.ValidateTarget(LocalTargetInfo.Invalid, false));
            PawnDiaryRimTestScope.Require(!accepted,
                "The real shuttle validator unexpectedly accepted an invalid target.");

            string[] excluded =
            {
                "TradeSettlement", "TradeOrbital", "TradeCaravan", "SteelDrop", "FoodDrop",
                "SilverDrop", "GlitterMedDrop", "CallLaborerTeam", "CallLaborerGang"
            };
            for (int i = 0; i < excluded.Length; i++)
            {
                RoyalTitlePermitDef def = RequirePermit(excluded[i]);
                FactionPermit permit = AddPermitFixture(def, faction);
                scope.RequireNoNewEvent(() =>
                {
                    pawn.royalty.GetPermit(def, faction);
                    permit.Notify_Used();
                });
            }
        }

        /// <summary>
        /// Synthetic modded permits remain fail-closed unless their Def identity is on the reviewed
        /// XML allowlist. Familiar English wording and malformed IDs cannot imitate a dramatic family.
        /// </summary>
        [Test]
        public static void SyntheticModdedPermitsRequireExactReviewedDefIdentity()
        {
            if (!RequireRoyaltyOrSkip(nameof(SyntheticModdedPermitsRequireExactReviewedDefIdentity))) return;
            Faction faction = RequireEmpire();
            RoyalTitleDef title = RequirePermit("CallOrbitalStrike").minTitle;
            AddRoyalTitleFixture(faction, title);
            RoyalTitlePermitDef[] syntheticDefs =
            {
                new RoyalTitlePermitDef
                {
                    defName = "Phase8_ModOrbitalLookingPermit",
                    label = "call orbital strike",
                    description = "Display wording deliberately resembles a reviewed permit.",
                    minTitle = title
                },
                new RoyalTitlePermitDef
                {
                    defName = "Phase8;MalformedPermit",
                    label = "military aid",
                    description = "An unsafe identity must fail closed.",
                    minTitle = title
                }
            };

            for (int i = 0; i < syntheticDefs.Length; i++)
            {
                RoyalTitlePermitDef def = syntheticDefs[i];
                FactionPermit permit = AddPermitFixture(def, faction);
                scope.RequireNoNewEvent(() =>
                {
                    PawnDiaryRimTestScope.Require(
                        ReferenceEquals(pawn.royalty.GetPermit(def, faction), permit),
                        "The live tracker did not return synthetic permit " + def.defName + ".");
                    permit.Notify_Used();
                });
                PawnDiaryRimTestScope.Require(RoyalPermitOwnerCache.SessionCountForTests == 0,
                    "An unreviewed synthetic permit entered the transient owner cache.");
            }
        }

        /// <summary>The immediate second success is suppressed by the XML-owned repeat window.</summary>
        [Test]
        public static void RepeatSuccessfulUseIsSuppressed()
        {
            if (!RequireRoyaltyOrSkip(nameof(RepeatSuccessfulUseIsSuppressed))) return;
            Faction faction = RequireEmpire();
            RoyalTitlePermitDef def = RequirePermit("CallOrbitalStrike");
            AddRoyalTitleFixture(faction, def.minTitle);
            FactionPermit permit = AddPermitFixture(def, faction);
            pawn.royalty.GetPermit(def, faction);
            scope.FireAndRequireEvent(
                () => permit.Notify_Used(), RoyalPermitPolicy.OrbitalStrikeEventDefName, pawn, null);
            scope.RequireNoNewEvent(() => permit.Notify_Used());
        }

        /// <summary>Exact faction/map military aid consumes its staged RaidFriendly in both orders.</summary>
        [Test]
        public static void QuickAidRaidBelongsToSuccessfulPermit()
        {
            if (!RequireRoyaltyOrSkip(nameof(QuickAidRaidBelongsToSuccessfulPermit))) return;
            Faction faction = RequireEmpire();
            RoyalTitlePermitDef def = RequirePermit("CallMilitaryAidSmall");
            AddRoyalTitleFixture(faction, def.minTitle);
            FactionPermit permit = AddPermitFixture(def, faction);
            scope.SpawnAsLiveColonist(pawn);
            pawn.royalty.GetPermit(def, faction);
            int fallbacks = 0;
            int genericSubmissions = 0;
            QuickMilitaryAidRaidCorrelation.SetDispatchOverrideForTests(_ => fallbacks++);
            RaidExecutePatch.SetRaidSubmitOverrideForTests(_ => genericSubmissions++);

            FireFriendlyRaidProductionPostfix(faction);
            PawnDiaryRimTestScope.Require(QuickMilitaryAidRaidCorrelation.PendingCountForTests == 1,
                "The production RaidExecutePatch did not stage the quick-aid raid.");
            PawnDiaryRimTestScope.Require(genericSubmissions == 0,
                "The production RaidExecutePatch leaked the staged raid into generic fan-out.");
            DiaryEvent permitEvent = scope.FireAndRequireEvent(
                () => permit.Notify_Used(), RoyalPermitPolicy.MilitaryAidEventDefName, pawn, null, true);
            scope.RequireSoloRef(permitEvent, pawn);
            PawnDiaryRimTestScope.Require(
                QuickMilitaryAidRaidCorrelation.PendingCountForTests == 0
                    && fallbacks == 0 && genericSubmissions == 0,
                "The successful permit did not consume its staged generic friendly raid.");

            // Modded reverse order: a second exact success is page-deduped but still owns its source;
            // the later same-faction/map quick raid consumes that recent owner instead of staging.
            scope.RequireNoNewEvent(() => permit.Notify_Used());
            PawnDiaryRimTestScope.Require(QuickMilitaryAidRaidCorrelation.RecentOwnerCountForTests == 1,
                "The reverse-order permit owner was not retained briefly.");
            FireFriendlyRaidProductionPostfix(faction);
            PawnDiaryRimTestScope.Require(QuickMilitaryAidRaidCorrelation.PendingCountForTests == 0
                    && QuickMilitaryAidRaidCorrelation.RecentOwnerCountForTests == 0
                    && fallbacks == 0 && genericSubmissions == 0,
                "A reverse-order quick-aid raid escaped its exact permit owner.");
        }

        /// <summary>The XML master leaves quick aid with the existing generic raid owner.</summary>
        [Test]
        public static void MasterPolicyDisabledLeavesQuickAidWithGenericRaidOwner()
        {
            if (!RequireRoyaltyOrSkip(nameof(MasterPolicyDisabledLeavesQuickAidWithGenericRaidOwner))) return;
            int genericSubmissions = 0;
            RaidExecutePatch.SetRaidSubmitOverrideForTests(_ => genericSubmissions++);
            livePolicy.enabled = false;

            FireFriendlyRaidProductionPostfix(RequireEmpire());

            PawnDiaryRimTestScope.Require(genericSubmissions == 1
                    && QuickMilitaryAidRaidCorrelation.PendingCountForTests == 0,
                "A master-disabled Royalty policy swallowed or staged the mature RaidFriendly story.");
        }

        /// <summary>A disabled permit output group still owns its exact source and prevents a duplicate raid.</summary>
        [Test]
        public static void DisabledPermitGroupStillClaimsQuickAidSource()
        {
            if (!RequireRoyaltyOrSkip(nameof(DisabledPermitGroupStillClaimsQuickAidSource))) return;
            Faction faction = RequireEmpire();
            RoyalTitlePermitDef def = RequirePermit("CallMilitaryAidSmall");
            AddRoyalTitleFixture(faction, def.minTitle);
            FactionPermit permit = AddPermitFixture(def, faction);
            scope.SpawnAsLiveColonist(pawn);
            pawn.royalty.GetPermit(def, faction);
            PawnDiaryMod.Settings.SetGroupEnabled("royalPermitDramatic", false);
            int genericSubmissions = 0;
            RaidExecutePatch.SetRaidSubmitOverrideForTests(_ => genericSubmissions++);

            FireFriendlyRaidProductionPostfix(faction);
            PawnDiaryRimTestScope.Require(QuickMilitaryAidRaidCorrelation.PendingCountForTests == 1
                    && genericSubmissions == 0,
                "The disabled output group prevented its healthy source owner from staging quick aid.");
            scope.RequireNoNewEvent(() => permit.Notify_Used());
            PawnDiaryRimTestScope.Require(QuickMilitaryAidRaidCorrelation.PendingCountForTests == 0
                    && genericSubmissions == 0,
                "A disabled permit page leaked the same action into the generic friendly-raid owner.");
        }

        /// <summary>The compatibility fallback is non-reentrant and treats scan truncation as ambiguity.</summary>
        [Test]
        public static void FallbackOwnerScanIsNonReentrantAndCapSafe()
        {
            if (!RequireRoyaltyOrSkip(nameof(FallbackOwnerScanIsNonReentrantAndCapSafe))) return;
            Faction faction = RequireEmpire();
            RoyalTitlePermitDef def = RequirePermit("CallOrbitalStrike");
            AddRoyalTitleFixture(faction, def.minTitle);
            FactionPermit permit = AddPermitFixture(def, faction);
            scope.SpawnAsLiveColonist(pawn);
            Pawn extra = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(extra);
            RoyaltyPolicySnapshot policy = CopyPolicyForRuntimeTest(DiaryRoyaltyPolicy.Snapshot());
            policy.maximumPermitFallbackPawns = 2;
            policy.maximumPermitOwnersPerSession = 4;
            policy.permitOwnerCacheTicks = 2500;

            RoyalPermitOwnerCache.Reset();
            RoyalPermitOwnerCache.SetFallbackPawnsOverrideForTests(new[] { pawn });
            RoyalPermitOwnerResolution exact = RoyalPermitOwnerCache.Resolve(
                permit, Find.TickManager.TicksGame, policy);
            PawnDiaryRimTestScope.Require(exact != null && ReferenceEquals(exact.pawn, pawn),
                "The compatibility fallback could not prove the exact list-owned permit.");
            PawnDiaryRimTestScope.Require(RoyalPermitOwnerCache.SessionCountForTests == 0,
                "Fallback ownership re-entered the patched GetPermit observer and mutated its cache.");

            RoyalPermitOwnerCache.Reset();
            policy.maximumPermitFallbackPawns = 1;
            RoyalPermitOwnerCache.SetFallbackPawnsOverrideForTests(new[] { pawn, extra });
            PawnDiaryRimTestScope.Require(RoyalPermitOwnerCache.Resolve(
                    permit, Find.TickManager.TicksGame, policy) == null,
                "A truncated fallback scan selected from partial evidence instead of failing closed.");
        }

        /// <summary>Expiry and cap overflow return every unclaimed raid to the original owner in order.</summary>
        [Test]
        public static void QuickAidExpiryAndOverflowFallbackAreLossless()
        {
            if (!RequireRoyaltyOrSkip(nameof(QuickAidExpiryAndOverflowFallbackAreLossless))) return;
            Faction faction = RequireEmpire();
            int now = Find.TickManager.TicksGame;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            policy = CopyPolicyForRuntimeTest(policy);
            policy.maximumPendingQuickAid = 1;
            List<RaidFanoutSignal> returned = new List<RaidFanoutSignal>();
            QuickMilitaryAidRaidCorrelation.SetDispatchOverrideForTests(signal => returned.Add(signal));
            RaidFanoutSignal first = BuildFriendlyRaid(faction);
            RaidFanoutSignal second = BuildFriendlyRaid(faction);
            PawnDiaryRimTestScope.Require(
                QuickMilitaryAidRaidCorrelation.TryStageOrSuppress(first, now, policy)
                    && QuickMilitaryAidRaidCorrelation.TryStageOrSuppress(second, now, policy),
                "The lossless overflow fixture could not stage both raids.");
            PawnDiaryRimTestScope.Require(returned.Count == 1 && ReferenceEquals(returned[0], first)
                    && QuickMilitaryAidRaidCorrelation.PendingCountForTests == 1,
                "Pending-cap overflow did not return the oldest exact RaidSignal.");
            QuickMilitaryAidRaidCorrelation.FlushExpired(now + policy.quickAidCorrelationTicks, policy);
            PawnDiaryRimTestScope.Require(returned.Count == 2 && ReferenceEquals(returned[1], second)
                    && QuickMilitaryAidRaidCorrelation.PendingCountForTests == 0,
                "Expiry did not return the remaining exact RaidSignal through its generic owner.");
        }

        /// <summary>Pre-save flush is lossless; FinalizeInit drops all weak/transient cross-game state.</summary>
        [Test]
        public static void PreSaveFlushAndLoadResetClearTransientState()
        {
            if (!RequireRoyaltyOrSkip(nameof(PreSaveFlushAndLoadResetClearTransientState))) return;
            Faction faction = RequireEmpire();
            RoyalTitlePermitDef def = RequirePermit("CallMilitaryAidSmall");
            AddRoyalTitleFixture(faction, def.minTitle);
            AddPermitFixture(def, faction);
            pawn.royalty.GetPermit(def, faction);
            PawnDiaryRimTestScope.Require(RoyalPermitOwnerCache.SessionCountForTests == 1,
                "GetPermit did not populate the weak owner cache.");
            int returned = 0;
            QuickMilitaryAidRaidCorrelation.SetDispatchOverrideForTests(_ => returned++);
            PawnDiaryRimTestScope.Require(QuickMilitaryAidRaidCorrelation.TryStageOrSuppress(
                    BuildFriendlyRaid(faction), Find.TickManager.TicksGame,
                    DiaryRoyaltyPolicy.Snapshot()),
                "The pre-save quick-aid fixture did not stage.");
            scope.Component.FlushRoyalPermitRaidsBeforeSave();
            PawnDiaryRimTestScope.Require(returned == 1
                    && QuickMilitaryAidRaidCorrelation.PendingCountForTests == 0,
                "Pre-save flush lost or retained an unclaimed quick-aid raid.");

            pawn.royalty.GetPermit(def, faction);
            QuickMilitaryAidRaidCorrelation.SetDispatchOverrideForTests(_ => { });
            QuickMilitaryAidRaidCorrelation.TryStageOrSuppress(
                BuildFriendlyRaid(faction), Find.TickManager.TicksGame, DiaryRoyaltyPolicy.Snapshot());
            scope.Component.FinalizeInit();
            PawnDiaryRimTestScope.Require(RoyalPermitOwnerCache.SessionCountForTests == 0
                    && QuickMilitaryAidRaidCorrelation.PendingCountForTests == 0
                    && QuickMilitaryAidRaidCorrelation.RecentOwnerCountForTests == 0,
                "FinalizeInit left transient permit/raid ownership across the load boundary.");
        }

        /// <summary>No-Royalty profiles expose no hooks, collectors, pages, or transient ownership.</summary>
        [Test]
        public static void RoyaltyInactivePathIsSilent()
        {
            RequirePermitPromptStudioAvailability();
            if (ModsConfig.RoyaltyActive)
            {
                Log.Message("[Pawn Diary RimTest] SKIP RoyaltyInactivePathIsSilent: Royalty is active.");
                return;
            }
            PawnDiaryRimTestScope.Require(!DiaryRoyaltyPatches.PermitOwnerHookReady
                    && !DiaryRoyaltyPatches.PermitUseHookReady,
                "Royalty-inactive permit hooks reported themselves ready.");
            RoyalPermitOwnerCandidate owner;
            RoyalPermitUseSnapshot use;
            PawnDiaryRimTestScope.Require(
                !DlcContext.TryCaptureRoyalPermitOwnerCandidate(pawn, null, 0, out owner)
                    && !DlcContext.TryCaptureRoyalPermitUse(null, null, false, 0, out use),
                "Royalty-inactive permit collectors returned live data.");
            scope.RequireNoNewEvent(() => scope.Component.ObserveRoyalPermitUse(
                pawn,
                new RoyalPermitUseSnapshot
                {
                    ownerPawnId = pawn.GetUniqueLoadID(),
                    permitDefName = "CallMilitaryAidSmall",
                    permitFamilyToken = RoyalPermitFamilyTokens.MilitaryAid,
                    factionId = "Faction_Empire",
                    tick = Find.TickManager?.TicksGame ?? 0
                }));
            PawnDiaryRimTestScope.Require(RoyalPermitOwnerCache.SessionCountForTests == 0
                    && QuickMilitaryAidRaidCorrelation.PendingCountForTests == 0,
                "Royalty-inactive permit path retained transient ownership.");
        }

        private static Faction RequireEmpire()
        {
            PawnDiaryRimTestScope.Require(Faction.OfEmpire != null,
                "Royalty is active but the Empire faction is unavailable in the loaded game.");
            return Faction.OfEmpire;
        }

        private static RoyalTitlePermitDef RequirePermit(string defName)
        {
            RoyalTitlePermitDef def = DefDatabase<RoyalTitlePermitDef>.GetNamedSilentFail(defName);
            PawnDiaryRimTestScope.Require(def != null,
                "Royalty is active but installed permit Def '" + defName + "' is unavailable.");
            return def;
        }

        private static RoyalTitleDef HighestRequiredTitle(IEnumerable<RoyalTitlePermitDef> defs)
        {
            return defs.Where(def => def?.minTitle != null)
                .Select(def => def.minTitle)
                .OrderByDescending(title => title.seniority)
                .FirstOrDefault();
        }

        private static void AddRoyalTitleFixture(Faction faction, RoyalTitleDef title)
        {
            if (title == null) return;
            RoyalTitle row = new RoyalTitle
            {
                def = title,
                faction = faction,
                pawn = pawn,
                receivedTick = Find.TickManager?.TicksGame ?? 0
            };
            pawn.royalty.AllTitlesForReading.Add(row);
            scope.RegisterCleanup(() => pawn?.royalty?.AllTitlesForReading?.Remove(row));
        }

        private static FactionPermit AddPermitFixture(RoyalTitlePermitDef def, Faction faction)
        {
            FactionPermit permit = new FactionPermit(
                faction,
                pawn.royalty.GetCurrentTitle(faction) ?? def.minTitle,
                def);
            pawn.royalty.AllFactionPermits.Add(permit);
            scope.RegisterCleanup(() => pawn?.royalty?.AllFactionPermits?.Remove(permit));
            return permit;
        }

        private static RaidFanoutSignal BuildFriendlyRaid(Faction faction)
        {
            Map map = Find.CurrentMap;
            IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail("RaidFriendly");
            PawnDiaryRimTestScope.Require(map != null && def != null,
                "Quick-aid acceptance needs a loaded map and RaidFriendly IncidentDef.");
            RaidFanoutSignal signal = new RaidFanoutSignal(
                new IncidentParms
                {
                    target = map,
                    faction = faction,
                    points = 100f,
                    raidArrivalModeForQuickMilitaryAid = true
                },
                def);
            PawnDiaryRimTestScope.Require(signal.IsValid,
                "The loaded RaidFriendly fixture did not build a valid RaidSignal.");
            return signal;
        }

        private static void FireFriendlyRaidProductionPostfix(Faction faction)
        {
            IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail("RaidFriendly");
            IncidentWorker worker = def?.Worker;
            PawnDiaryRimTestScope.Require(worker is IncidentWorker_RaidFriendly,
                "RaidFriendly did not expose the exact production IncidentWorker_RaidFriendly.");
            RaidExecutePatch.Postfix(
                worker,
                new IncidentParms
                {
                    target = Find.CurrentMap,
                    faction = faction,
                    points = 100f,
                    raidArrivalModeForQuickMilitaryAid = true
                },
                true);
        }

        private static void RequirePermitPromptStudioAvailability()
        {
            MethodInfo method = typeof(PawnDiaryMod).GetMethod(
                "EventPromptDefsForSettings", BindingFlags.Static | BindingFlags.NonPublic);
            List<DiaryEventPromptDef> visible = method?.Invoke(null, null) as List<DiaryEventPromptDef>;
            PawnDiaryRimTestScope.Require(visible != null,
                "The Prompt Studio event-policy availability seam was unavailable.");
            string[] defNames =
            {
                "DiaryEventPrompt_RoyalPermit", "DiaryEventPrompt_RoyalPermitMilitaryAid",
                "DiaryEventPrompt_RoyalPermitTransportShuttle", "DiaryEventPrompt_RoyalPermitOrbitalStrike",
                "DiaryEventPrompt_RoyalPermitOrbitalSalvo"
            };
            for (int i = 0; i < defNames.Length; i++)
            {
                bool shown = visible.Any(def => def != null && def.defName == defNames[i]);
                PawnDiaryRimTestScope.Require(shown == ModsConfig.RoyaltyActive,
                    defNames[i] + " Prompt Studio availability did not match the active Royalty package.");
            }
        }

        private static RoyaltyPolicySnapshot CopyPolicyForRuntimeTest(RoyaltyPolicySnapshot source)
        {
            // Only arbitration windows/caps are read by this fixture; do not mutate the cached Def snapshot.
            return new RoyaltyPolicySnapshot
            {
                quickAidCorrelationTicks = source.quickAidCorrelationTicks,
                maximumPendingQuickAid = source.maximumPendingQuickAid,
                maximumRecentQuickAidOwners = source.maximumRecentQuickAidOwners,
                maximumPermitFallbackPawns = source.maximumPermitFallbackPawns,
                maximumPermitOwnersPerSession = source.maximumPermitOwnersPerSession,
                permitOwnerCacheTicks = source.permitOwnerCacheTicks
            };
        }

        private static void RequireContext(DiaryEvent diaryEvent, string fragment)
        {
            PawnDiaryRimTestScope.Require(diaryEvent?.gameContext != null
                    && diaryEvent.gameContext.IndexOf(fragment, StringComparison.Ordinal) >= 0,
                "Expected permit context fragment '" + fragment + "', got '"
                    + (diaryEvent?.gameContext ?? "<null>") + "'.");
        }

        private static void RequireOwnedPatch(
            MethodBase target,
            string prefixName,
            string postfixName,
            Type patchType = null)
        {
            Type ownerType = patchType ?? typeof(DiaryRoyaltyPatches);
            Patches patches = target == null ? null : Harmony.GetPatchInfo(target);
            PawnDiaryRimTestScope.Require(target != null && patches != null,
                "Expected a patched Royalty permit target, but the exact method was unavailable: "
                    + target + ".");
            if (prefixName != null)
                PawnDiaryRimTestScope.Require(patches.Prefixes.Any(row => row.owner == "aimml.pawndiary"
                        && row.PatchMethod?.DeclaringType == ownerType
                        && row.PatchMethod.Name == prefixName),
                    "Expected Pawn Diary's " + prefixName + " on " + target + ".");
            if (postfixName != null)
                PawnDiaryRimTestScope.Require(patches.Postfixes.Any(row => row.owner == "aimml.pawndiary"
                        && row.PatchMethod?.DeclaringType == ownerType
                        && row.PatchMethod.Name == postfixName),
                    "Expected Pawn Diary's " + postfixName + " on " + target + ".");
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

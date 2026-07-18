// Biotech Phase-6 mechanitor orchestration. Exact hooks call this partial component after guarded
// DlcContext projection; pure policy owns name/tenure/Tale decisions, while this file mutates saved
// observation and emits ordinary solo Progression pages for the human mechanitor POV.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Live call-local ownership captured before an Overseer relation is added.</summary>
    internal sealed class MechanitorRelationCallState
    {
        public Pawn controller;
        public Pawn mech;
    }

    public partial class DiaryGameComponent
    {
        /// <summary>Slow old-save baseline and missed-hook tenure maintenance; never emits a page.</summary>
        private void ObserveMechanitorState(Pawn controller, PawnProgressionState progression)
        {
            MechanitorControllerSnapshot snapshot;
            if (progression == null
                || !DlcContext.TryCaptureMechanitorController(controller, out snapshot)) return;
            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            // This hourly batch may contain many mechs. Initialize the narrow nested row directly and
            // normalize it once at the XML cap before the loop; calling the general Ensure methods here
            // would normalize the same list repeatedly before we even visit its members.
            BiotechPawnProgressionState biotech = progression.biotechProgressionState;
            if (biotech == null)
            {
                biotech = new BiotechPawnProgressionState();
                progression.biotechProgressionState = biotech;
            }
            MechanitorObservationState state = biotech.mechanitorObservation;
            if (state == null)
            {
                state = new MechanitorObservationState();
                biotech.mechanitorObservation = state;
            }
            state.Normalize(policy.mechanitorMaximumObservedMechs, policy.mechanitorMaximumBossCalls);
            int tick = Find.TickManager.TicksGame;
            if (!state.IsInitialized())
            {
                state.Baseline(snapshot, tick, policy.mechanitorMaximumObservedMechs);
                return;
            }

            state.mechlinkPresent = snapshot.hasMechlink;
            for (int i = 0; i < snapshot.overseenMechs.Count; i++)
                state.ObserveMech(snapshot.overseenMechs[i], tick, policy.mechanitorMaximumObservedMechs);
        }

        /// <summary>Records one exact successful mechlink installation.</summary>
        internal void OnMechanitorMechlinkInstalled(Pawn controller)
        {
            if (!GamePlaying || !ModsConfig.BiotechActive || !IsDiaryEligible(controller)) return;
            MechanitorObservationState state = MechanitorStateFor(controller, true);
            if (state == null || state.mechlinkPresent) return;
            state.observationVersion = MechanitorObservationState.CurrentVersion;
            state.mechlinkPresent = true;
            EmitMechanitorProgression(
                controller,
                MechanitorEventDefNames.MechlinkInstalled,
                "mechlink_installed",
                "PawnDiary.Event.Biotech.Mechanitor.MechlinkInstalled.Label".Translate().Resolve(),
                "PawnDiary.Event.Biotech.Mechanitor.MechlinkInstalled.Text"
                    .Translate(controller.LabelShortCap).Resolve(),
                MechanitorContextKeys.MechanitorMoment + "=mechlink_installed; mechanitor="
                    + controller.LabelShortCap,
                "mechanitor-mechlink-install|" + controller.GetUniqueLoadID());
        }

        /// <summary>Records exact mechlink removal unless it is merely part of the controller's death.</summary>
        internal void OnMechanitorMechlinkRemoving(Pawn controller)
        {
            if (!GamePlaying || !ModsConfig.BiotechActive || controller == null
                || MechanitorDeathScope.IsDying(controller) || controller.Dead
                || !IsDiaryEligible(controller)) return;
            MechanitorObservationState state = MechanitorStateFor(controller, true);
            if (state == null) return;
            if (state.IsInitialized() && !state.mechlinkPresent) return;
            state.observationVersion = MechanitorObservationState.CurrentVersion;
            state.mechlinkPresent = false;
            EmitMechanitorProgression(
                controller,
                MechanitorEventDefNames.MechlinkRemoved,
                "mechlink_removed",
                "PawnDiary.Event.Biotech.Mechanitor.MechlinkRemoved.Label".Translate().Resolve(),
                "PawnDiary.Event.Biotech.Mechanitor.MechlinkRemoved.Text"
                    .Translate(controller.LabelShortCap).Resolve(),
                MechanitorContextKeys.MechanitorMoment + "=mechlink_removed; mechanitor="
                    + controller.LabelShortCap,
                "mechanitor-mechlink-remove|" + controller.GetUniqueLoadID());
        }

        /// <summary>Baselines existing control before vanilla adds a new Overseer relation.</summary>
        internal MechanitorRelationCallState BeginMechanitorRelation(
            Pawn firstPawn,
            Pawn otherPawn,
            PawnRelationDef relationDef)
        {
            if (!GamePlaying || !ModsConfig.BiotechActive || firstPawn == null || otherPawn == null
                || !string.Equals(relationDef?.defName, "Overseer", StringComparison.OrdinalIgnoreCase))
                return null;
            Pawn controller = firstPawn.mechanitor != null ? firstPawn
                : otherPawn.mechanitor != null ? otherPawn : null;
            Pawn mech = controller == firstPawn ? otherPawn : firstPawn;
            if (!IsDiaryEligible(controller)) return null;

            PawnProgressionState progression = ProgressionStateForMechanitor(controller, true);
            if (progression == null) return null;
            MechanitorObservationState state = progression.EnsureBiotechState()
                .EnsureMechanitorObservation();
            if (!state.IsInitialized())
            {
                MechanitorControllerSnapshot baseline;
                if (DlcContext.TryCaptureMechanitorController(controller, out baseline))
                {
                    BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
                    state.Baseline(
                        baseline,
                        Find.TickManager.TicksGame,
                        policy.mechanitorMaximumObservedMechs);
                }
                else
                {
                    state.observationVersion = MechanitorObservationState.CurrentVersion;
                    state.mechlinkPresent = controller.mechanitor != null;
                }
            }
            return new MechanitorRelationCallState { controller = controller, mech = mech };
        }

        /// <summary>Owns the first exact Overseer relation and records at most one first-mech page.</summary>
        internal void CompleteMechanitorRelation(MechanitorRelationCallState call)
        {
            if (call?.controller == null || call.mech == null || !ModsConfig.BiotechActive) return;
            MechanitorMechSnapshot mech;
            if (!DlcContext.TryCaptureMechanitorMech(call.controller, call.mech, out mech)) return;
            MechanitorObservationState state = MechanitorStateFor(call.controller, true);
            if (state == null) return;
            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            state.ObserveMech(mech, Find.TickManager.TicksGame, policy.mechanitorMaximumObservedMechs);
            if (state.firstControlledPageConsumed) return;
            // Observation is truth, while page creation is optional output. Consume the exact first
            // relation even when this pawn's generation toggle is currently disabled.
            state.firstControlledPageConsumed = true;

            string context = MechanitorMechContext("first_controlled_mech", mech, 0, false);
            EmitMechanitorProgression(
                call.controller,
                MechanitorEventDefNames.FirstControlledMech,
                "first_controlled_mech",
                "PawnDiary.Event.Biotech.Mechanitor.FirstMech.Label".Translate().Resolve(),
                "PawnDiary.Event.Biotech.Mechanitor.FirstMech.Text"
                    .Translate(call.controller.LabelShortCap, mech.displayName).Resolve(),
                context,
                "mechanitor-first-mech|" + call.controller.GetUniqueLoadID());
        }

        /// <summary>
        /// Observes the first configured hostile combat Tale whose exact instigator is a currently
        /// controlled mech. A successful controller page suppresses the ordinary Tale route; disabled
        /// output still consumes the exact occurrence but fails open so generic ownership remains.
        /// </summary>
        internal bool TryHandleMechanitorCombatTale(Tale tale, TaleDef taleDef)
        {
            if (!GamePlaying || !ModsConfig.BiotechActive || tale == null || taleDef == null) return false;
            Pawn first;
            Pawn second;
            ExtractMechanitorTalePawns(tale, out first, out second);
            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            int role = MechanitorLifecyclePolicy.CombatInstigatorRole(
                taleDef.defName,
                policy.mechanitorCombatFirstPawnDefNames,
                policy.mechanitorCombatSecondPawnDefNames);
            Pawn mech = role == 1 ? first : role == 2 ? second : null;
            Pawn target = role == 1 ? second : role == 2 ? first : null;
            Pawn controller;
            MechanitorMechSnapshot snapshot;
            if (!DlcContext.TryCaptureControlledMech(mech, out controller, out snapshot)
                || !snapshot.controlled || !IsDiaryEligible(controller)
                || target == null || !target.HostileTo(controller)) return false;

            MechanitorObservationState state = MechanitorStateFor(controller, true);
            if (state == null || state.firstControlledCombatPageConsumed) return false;
            state.ObserveMech(snapshot, Find.TickManager.TicksGame, policy.mechanitorMaximumObservedMechs);
            // As with first control, the exact hostile combat occurrence is consumed independently
            // from optional output. The return value still controls whether generic Tale ownership
            // should be suppressed for this invocation.
            state.firstControlledCombatPageConsumed = true;
            string context = MechanitorMechContext("first_controlled_mech_combat", snapshot, 0, false)
                + "; " + MechanitorContextKeys.CombatTale + "=" + taleDef.defName;
            if (target != null)
                context += "; " + MechanitorContextKeys.CombatTarget + "="
                    + MechanitorContextValue(target.LabelShortCap);
            bool emitted = EmitMechanitorProgression(
                controller,
                MechanitorEventDefNames.FirstControlledMechCombat,
                "first_controlled_mech_combat",
                "PawnDiary.Event.Biotech.Mechanitor.FirstCombat.Label".Translate().Resolve(),
                "PawnDiary.Event.Biotech.Mechanitor.FirstCombat.Text"
                    .Translate(controller.LabelShortCap, snapshot.displayName).Resolve(),
                context,
                "mechanitor-first-combat|" + controller.GetUniqueLoadID());
            return emitted;
        }

        /// <summary>Snapshots and records a custom-named or observed long-serving mech loss.</summary>
        internal void OnControlledMechDying(Pawn mech)
        {
            Pawn controller;
            MechanitorMechSnapshot snapshot;
            if (!GamePlaying || !ModsConfig.BiotechActive
                || !DlcContext.TryCaptureControlledMech(mech, out controller, out snapshot)
                || !snapshot.controlled || !IsDiaryEligible(controller)) return;
            MechanitorObservationState state = MechanitorStateFor(controller, true);
            if (state == null) return;
            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            int now = Find.TickManager.TicksGame;
            MechanitorMechObservationState observed = state.ObserveMech(
                snapshot, now, policy.mechanitorMaximumObservedMechs);
            if (observed == null || observed.lossObserved) return;
            observed.lossObserved = true;
            int serviceTicks = Math.Max(0, now - observed.firstObservedTick);
            bool longServing = MechanitorLifecyclePolicy.IsLongServing(
                observed.firstObservedTick, now, policy.mechanitorLongServiceTicks);
            if (!MechanitorLifecyclePolicy.ShouldRecordLoss(
                snapshot, observed.firstObservedTick, now, policy.mechanitorLongServiceTicks)) return;
            string context = MechanitorMechContext(
                "significant_mech_loss", snapshot, serviceTicks, longServing);
            string deathFacts = DeathContextCache.ConsumeOrBuild(mech);
            if (!string.IsNullOrWhiteSpace(deathFacts)) context += "; " + deathFacts;
            EmitMechanitorProgression(
                controller,
                MechanitorEventDefNames.SignificantMechLoss,
                "significant_mech_loss",
                "PawnDiary.Event.Biotech.Mechanitor.MechLoss.Label".Translate().Resolve(),
                "PawnDiary.Event.Biotech.Mechanitor.MechLoss.Text"
                    .Translate(controller.LabelShortCap, snapshot.displayName).Resolve(),
                context,
                "mechanitor-mech-loss|" + controller.GetUniqueLoadID() + "|" + snapshot.mechId);
        }

        /// <summary>Saves and narrates one boss threat only after vanilla confirms the call.</summary>
        internal void OnMechanitorBossCalled(Pawn caller, BossgroupDef bossgroup)
        {
            if (!GamePlaying || !ModsConfig.BiotechActive || bossgroup?.boss == null
                || !IsDiaryEligible(caller)) return;
            MechanitorObservationState state = MechanitorStateFor(caller, true);
            if (state == null) return;
            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            // The XML cap is an admission limit for pending exact ownership. Never evict an older
            // unresolved caller merely because tuning was lowered or many threats are outstanding.
            state.Normalize(
                MechanitorObservationState.HardMaximumMechs,
                MechanitorObservationState.HardMaximumBossCalls);
            for (int i = state.bossCalls.Count - 1;
                i >= 0 && state.bossCalls.Count >= policy.mechanitorMaximumBossCalls;
                i--)
            {
                if (state.bossCalls[i]?.defeatedObserved == true) state.bossCalls.RemoveAt(i);
            }
            if (state.bossCalls.Count >= policy.mechanitorMaximumBossCalls) return;
            int tick = Find.TickManager.TicksGame;
            MechanitorBossCallObservationState row = new MechanitorBossCallObservationState
            {
                bossgroupDefName = bossgroup.defName ?? string.Empty,
                bossDefName = bossgroup.boss.defName ?? string.Empty,
                bossKindDefName = bossgroup.boss.kindDef?.defName ?? string.Empty,
                bossLabel = DiaryLineCleaner.CleanLine(bossgroup.boss.kindDef?.LabelCap.Resolve()),
                calledTick = tick
            };
            state.bossCalls.Add(row);
            string context = BossContext("boss_called", row, called: true, defeated: false);
            EmitMechanitorProgression(
                caller,
                MechanitorEventDefNames.BossCalled,
                "boss_called",
                "PawnDiary.Event.Biotech.Mechanitor.BossCalled.Label".Translate().Resolve(),
                "PawnDiary.Event.Biotech.Mechanitor.BossCalled.Text"
                    .Translate(caller.LabelShortCap, row.bossLabel).Resolve(),
                context,
                "mechanitor-boss-called|" + caller.GetUniqueLoadID() + "|"
                    + row.bossgroupDefName + "|" + tick);
        }

        /// <summary>Correlates an exact spawned boss pawn to one globally oldest pending call.</summary>
        internal void OnMechanitorBossSpawned(Pawn bossPawn)
        {
            if (!GamePlaying || !ModsConfig.BiotechActive || bossPawn?.kindDef == null) return;
            List<MechanitorBossOwnershipCandidate> candidates = BossOwnershipCandidates();
            MechanitorLifecyclePolicy.AssignSpawnedBoss(
                candidates,
                bossPawn.kindDef.defName,
                bossPawn.GetUniqueLoadID());
        }

        /// <summary>
        /// Applies a verified boss death to its exact spawned-pawn ownership row. The page states only
        /// that the called threat was defeated; it never invents who struck the final blow.
        /// </summary>
        internal void OnMechanitorBossDefeated(Pawn bossPawn)
        {
            if (!GamePlaying || !ModsConfig.BiotechActive || bossPawn?.kindDef == null || diaries == null)
                return;
            MechanitorBossOwnershipCandidate candidate = MechanitorLifecyclePolicy.FindDefeatedBoss(
                BossOwnershipCandidates(),
                bossPawn.kindDef.defName,
                bossPawn.GetUniqueLoadID());
            MechanitorBossCallObservationState row = candidate?.call;
            if (row == null) return;
            row.defeatedObserved = true;
            Pawn caller = FindLivePawnByLoadId(candidate.ownerId);
            if (!IsDiaryEligible(caller)) return;
            EmitMechanitorProgression(
                caller,
                MechanitorEventDefNames.BossDefeated,
                "boss_defeated",
                "PawnDiary.Event.Biotech.Mechanitor.BossDefeated.Label".Translate().Resolve(),
                "PawnDiary.Event.Biotech.Mechanitor.BossDefeated.Text"
                    .Translate(caller.LabelShortCap, row.bossLabel).Resolve(),
                BossContext("boss_defeated", row, called: true, defeated: true),
                "mechanitor-boss-defeated|" + caller.GetUniqueLoadID() + "|"
                    + row.bossDefName + "|" + row.calledTick);
        }

        private List<MechanitorBossOwnershipCandidate> BossOwnershipCandidates()
        {
            List<MechanitorBossOwnershipCandidate> candidates =
                new List<MechanitorBossOwnershipCandidate>();
            if (diaries == null) return candidates;
            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                List<MechanitorBossCallObservationState> calls = diary?.progressionState?
                    .biotechProgressionState?.mechanitorObservation?.bossCalls;
                if (calls == null) continue;
                for (int j = 0; j < calls.Count; j++)
                {
                    if (calls[j] == null) continue;
                    candidates.Add(new MechanitorBossOwnershipCandidate
                    {
                        ownerId = diary.pawnId ?? string.Empty,
                        call = calls[j]
                    });
                }
            }
            return candidates;
        }

        private MechanitorObservationState MechanitorStateFor(Pawn controller, bool create)
        {
            PawnProgressionState progression = ProgressionStateForMechanitor(controller, create);
            return progression?.EnsureBiotechState().EnsureMechanitorObservation();
        }

        private PawnProgressionState ProgressionStateForMechanitor(Pawn controller, bool create)
        {
            if (controller == null) return null;
            PawnDiaryRecord diary = FindDiary(controller, create);
            return diary?.EnsureProgressionState();
        }

        private bool EmitMechanitorProgression(
            Pawn controller,
            string defName,
            string kind,
            string label,
            string text,
            string context,
            string dedupKey)
        {
            if (controller == null) return false;
            ProgressionEventData data = ProgressionData(
                controller, defName, kind, label, string.Empty, label, context);
            return DispatchProgression(
                controller,
                data,
                label,
                text,
                majorArcCandidate: false,
                dedupKey: dedupKey,
                dedupWindowTicks: DiaryTuning.Current.genericEventTypeDedupTicks);
        }

        private static string MechanitorMechContext(
            string moment,
            MechanitorMechSnapshot mech,
            int serviceTicks,
            bool longServing)
        {
            return MechanitorContextKeys.MechanitorMoment + "=" + moment
                + "; " + MechanitorContextKeys.MechId + "=" + MechanitorContextValue(mech?.mechId)
                + "; " + MechanitorContextKeys.MechName + "=" + MechanitorContextValue(mech?.displayName)
                + "; " + MechanitorContextKeys.MechKind + "=" + MechanitorContextValue(mech?.kindLabel)
                + "; " + MechanitorContextKeys.PlayerNamed + "="
                    + (MechanitorLifecyclePolicy.IsPlayerNamed(mech) ? "true" : "false")
                + "; " + MechanitorContextKeys.ServiceTicks + "=" + Math.Max(0, serviceTicks)
                + "; " + MechanitorContextKeys.LongServing + "=" + (longServing ? "true" : "false");
        }

        private static string BossContext(
            string moment,
            MechanitorBossCallObservationState row,
            bool called,
            bool defeated)
        {
            return MechanitorContextKeys.MechanitorMoment + "=" + moment
                + "; " + MechanitorContextKeys.BossgroupDef + "="
                    + MechanitorContextValue(row.bossgroupDefName)
                + "; " + MechanitorContextKeys.BossDef + "=" + MechanitorContextValue(row.bossDefName)
                + "; " + MechanitorContextKeys.BossLabel + "=" + MechanitorContextValue(row.bossLabel)
                + "; " + MechanitorContextKeys.CalledTick + "=" + row.calledTick
                + "; " + MechanitorContextKeys.ThreatCalled + "=" + (called ? "true" : "false")
                + "; " + MechanitorContextKeys.ThreatDefeated + "=" + (defeated ? "true" : "false");
        }

        private static string MechanitorContextValue(string value)
        {
            return DiaryLineCleaner.CleanLine(value).Replace(";", ",");
        }

        private static void ExtractMechanitorTalePawns(Tale tale, out Pawn first, out Pawn second)
        {
            first = null;
            second = null;
            Tale_DoublePawn doublePawn = tale as Tale_DoublePawn;
            if (doublePawn != null)
            {
                first = doublePawn.firstPawnData?.pawn;
                second = doublePawn.secondPawnData?.pawn;
                return;
            }
            Tale_SinglePawn singlePawn = tale as Tale_SinglePawn;
            if (singlePawn != null) first = singlePawn.pawnData?.pawn;
        }
    }
}

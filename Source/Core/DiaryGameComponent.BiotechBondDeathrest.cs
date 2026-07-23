// Biotech Phase-8 psychic-bond and interrupted-deathrest orchestration. Harmony hooks provide
// guarded event-time snapshots; pure policy owns epochs, causes, severe bands, cooldowns, and
// lifetime limits; this component mutates only bounded per-pawn save rows and dispatches pages.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>Returns the next persistent epoch for a canonical pair before BondTo mutates it.</summary>
        internal int NextPsychicBondEpoch(Pawn first, Pawn second)
        {
            if (!GamePlaying || !ModsConfig.BiotechActive || first == null || second == null) return 1;
            BiotechPawnProgressionState firstState = BiotechStateFor(first, false);
            BiotechPawnProgressionState secondState = BiotechStateFor(second, false);
            return PsychicBondLifecyclePolicy.NextEpoch(
                first.GetUniqueLoadID(),
                second.GetUniqueLoadID(),
                firstState?.psychicBondObservations,
                secondState?.psychicBondObservations);
        }

        /// <summary>Returns the frozen current epoch for RemoveBond, baselining an unseen live pair.</summary>
        internal int CurrentPsychicBondEpoch(Pawn first, Pawn second)
        {
            if (!GamePlaying || !ModsConfig.BiotechActive || first == null || second == null) return 1;
            string firstId = first.GetUniqueLoadID();
            string secondId = second.GetUniqueLoadID();
            int firstEpoch = BondEpochFor(BiotechStateFor(first, false), secondId);
            int secondEpoch = BondEpochFor(BiotechStateFor(second, false), firstId);
            return Math.Max(1, Math.Max(firstEpoch, secondEpoch));
        }

        /// <summary>
        /// Commits one verified formation/rupture, updates both eligible POV histories, and dispatches
        /// at most one pair-shaped catalog signal. A disabled group still owns its exact companions.
        /// </summary>
        internal bool CompletePsychicBondTransition(
            Pawn first,
            Pawn second,
            PsychicBondMutationSnapshot snapshot)
        {
            if (!GamePlaying || !ModsConfig.BiotechActive || snapshot == null) return false;
            bool formation = PsychicBondLifecyclePolicy.ShouldOwnFormation(snapshot);
            bool rupture = PsychicBondLifecyclePolicy.ShouldOwnRupture(snapshot);
            if (!formation && !rupture) return false;
            UpdateBondObservation(first, second, snapshot, formation);
            UpdateBondObservation(second, first, snapshot, formation);

            Pawn sortedFirst = string.Equals(
                first?.GetUniqueLoadID(),
                snapshot.firstPawnId,
                StringComparison.Ordinal) ? first : second;
            Pawn sortedSecond = ReferenceEquals(sortedFirst, first) ? second : first;
            if (sortedFirst == null || sortedSecond == null) return false;
            BiotechBondEventData data = new BiotechBondEventData
            {
                Tick = snapshot.observedTick,
                DefName = formation
                    ? BiotechBondDeathrestEventDefNames.PsychicBondFormed
                    : BiotechBondDeathrestEventDefNames.PsychicBondRuptured,
                FirstPawnId = snapshot.firstPawnId,
                SecondPawnId = snapshot.secondPawnId,
                BondEpoch = snapshot.bondEpoch,
                Phase = snapshot.phase,
                FirstPawnEligible = snapshot.firstPawnEligible,
                SecondPawnEligible = snapshot.secondPawnEligible,
                HasVerifiedTransition = true
            };
            Dispatch(new BiotechBondSignal(data, snapshot, sortedFirst, sortedSecond));
            return true;
        }

        /// <summary>Applies severe/cooldown/lifetime policy after Wake confirms InterruptedDeathrest.</summary>
        internal bool CompleteInterruptedDeathrest(
            Pawn pawn,
            DeathrestMutationSnapshot snapshot)
        {
            if (!GamePlaying || !ModsConfig.BiotechActive || pawn == null || snapshot == null)
                return false;
            BiotechPolicySnapshot biotechPolicy = DiaryBiotechPolicy.Snapshot();
            BiotechPawnProgressionState biotech = snapshot.pawnEligible
                ? BiotechStateFor(pawn, true)
                : null;
            DeathrestObservationState state = biotech?.EnsureDeathrestObservation();
            DeathrestInterruptionDecision decision = DeathrestInterruptionPolicy.Decide(
                snapshot,
                state,
                biotechPolicy.bondDeathrest);
            if (decision == DeathrestInterruptionDecision.Drop) return false;
            if (biotech != null)
            {
                state.observationVersion = DeathrestInterruptionPolicy.CurrentObservationVersion;
            }
            if (decision != DeathrestInterruptionDecision.OwnAndRecord) return true;

            DeathrestInterruptionPolicy.Record(state, snapshot.observedTick);
            string phaseText = "PawnDiary.Event.Biotech.Deathrest.Phase.Interrupted".Translate().Resolve();
            string context = BiotechBondDeathrestContextKeys.DeathrestPhase + "=interrupted; "
                + BiotechBondDeathrestContextKeys.CompletionBand + "=severe";
            ProgressionEventData data = ProgressionData(
                pawn,
                BiotechBondDeathrestEventDefNames.DeathrestInterrupted,
                "deathrest_interrupted",
                phaseText,
                string.Empty,
                phaseText,
                context);
            DispatchProgression(
                pawn,
                data,
                "PawnDiary.Event.Biotech.Deathrest.Label".Translate().Resolve(),
                "PawnDiary.Event.Biotech.Deathrest.Text".Translate(pawn.LabelShortCap).Resolve(),
                majorArcCandidate: false,
                dedupKey: "biotech-deathrest-interrupted|" + snapshot.pawnId + "|"
                    + state.severeInterruptionsRecorded,
                dedupWindowTicks: biotechPolicy.bondDeathrest.deathrestCooldownTicks);
            return true;
        }

        /// <summary>Silently initializes old-save bond histories before any fallback observation emits.</summary>
        private void BaselineBiotechPsychicBondsOnLoad()
        {
            if (!ModsConfig.BiotechActive) return;
            List<Pawn> pawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;
            int tick = Find.TickManager?.TicksGame ?? 0;
            BiotechBondDeathrestPolicySnapshot policy = DiaryBiotechPolicy.Snapshot().bondDeathrest;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!IsDiaryEligible(pawn)) continue;
                BiotechPawnProgressionState state = BiotechStateFor(pawn, true);
                if (state == null) continue;
                PsychicBondLifecyclePolicy.NormalizeRows(
                    state.EnsurePsychicBondObservations(),
                    pawn.GetUniqueLoadID(),
                    tick,
                    policy.maximumBondObservationRows);
                if (state.bondObservationVersion >= PsychicBondLifecyclePolicy.CurrentObservationVersion)
                    continue;
                Pawn partner = DlcContext.PsychicBondPartner(pawn);
                if (partner != null && DlcContext.AreMutuallyPsychicBonded(pawn, partner))
                {
                    int epoch = CurrentPsychicBondEpoch(pawn, partner);
                    if (epoch < 1) epoch = 1;
                    UpsertBondRow(state, partner.GetUniqueLoadID(), epoch, true, tick, policy);
                    if (IsDiaryEligible(partner))
                    {
                        BiotechPawnProgressionState partnerState = BiotechStateFor(partner, true);
                        UpsertBondRow(
                            partnerState,
                            pawn.GetUniqueLoadID(),
                            epoch,
                            true,
                            tick,
                            policy);
                        partnerState.bondObservationVersion =
                            PsychicBondLifecyclePolicy.CurrentObservationVersion;
                    }
                }
                state.bondObservationVersion = PsychicBondLifecyclePolicy.CurrentObservationVersion;
            }
        }

        private BiotechPawnProgressionState BiotechStateFor(Pawn pawn, bool create)
        {
            if (pawn == null || (create && !IsDiaryEligible(pawn))) return null;
            PawnDiaryRecord diary = FindDiary(pawn, create);
            return diary?.EnsureProgressionState().EnsureBiotechState();
        }

        private void UpdateBondObservation(
            Pawn owner,
            Pawn partner,
            PsychicBondMutationSnapshot snapshot,
            bool bonded)
        {
            if (owner == null || partner == null || !IsDiaryEligible(owner)) return;
            BiotechPawnProgressionState state = BiotechStateFor(owner, true);
            if (state == null) return;
            BiotechBondDeathrestPolicySnapshot policy = DiaryBiotechPolicy.Snapshot().bondDeathrest;
            UpsertBondRow(
                state,
                partner.GetUniqueLoadID(),
                snapshot.bondEpoch,
                bonded,
                snapshot.observedTick,
                policy);
            state.bondObservationVersion = PsychicBondLifecyclePolicy.CurrentObservationVersion;
        }

        private static void UpsertBondRow(
            BiotechPawnProgressionState state,
            string partnerPawnId,
            int epoch,
            bool bonded,
            int tick,
            BiotechBondDeathrestPolicySnapshot policy)
        {
            if (state == null || string.IsNullOrWhiteSpace(partnerPawnId)) return;
            List<PsychicBondObservationRow> rows = state.EnsurePsychicBondObservations();
            PsychicBondObservationRow row = null;
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i]?.partnerPawnId, partnerPawnId, StringComparison.Ordinal))
                {
                    row = rows[i];
                    break;
                }
            }
            if (row == null)
            {
                row = new PsychicBondObservationRow { partnerPawnId = partnerPawnId };
                rows.Add(row);
            }
            row.bondEpoch = Math.Max(1, epoch);
            row.bonded = bonded;
            row.lastTransitionTick = Math.Max(0, tick);
            PsychicBondLifecyclePolicy.NormalizeRows(
                rows,
                string.Empty,
                tick,
                policy?.maximumBondObservationRows ?? 16);
        }

        private static int BondEpochFor(BiotechPawnProgressionState state, string partnerPawnId)
        {
            List<PsychicBondObservationRow> rows = state?.psychicBondObservations;
            int epoch = 0;
            for (int i = 0; i < (rows?.Count ?? 0); i++)
            {
                if (string.Equals(rows[i]?.partnerPawnId, partnerPawnId, StringComparison.Ordinal))
                    epoch = Math.Max(epoch, rows[i].bondEpoch);
            }
            return epoch;
        }
    }
}

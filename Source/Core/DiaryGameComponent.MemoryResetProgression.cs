// Saved progression cleanup for the Brainwipe memory boundary. These rows are observation cursors,
// not diary prose, but leaving their pre-wipe values behind can either resurrect an old loss later or
// make the pawn's next arrival anniversary count from a life they no longer remember. This partial
// keeps that bookkeeping reset beside the component that owns the saved PawnDiaryRecord graph.
using System;
using PawnDiary.Capture;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Re-baselines saved progression at one pawn's Brainwipe tick. The wipe becomes the pawn's new
        /// arrival epoch, old bonded deaths cannot be migrated or rediscovered, and unresolved Biotech
        /// boss calls cannot later produce a page about a pre-wipe decision. The nested Biotech row is
        /// plain saved state, so clearing it is safe even when the DLC is not active.
        /// </summary>
        private void ResetProgressionMemoryForPawn(string pawnId, int wipeTick)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            int boundaryTick = Math.Max(0, wipeTick);
            PawnProgressionState progression = FindDiaryByPawnId(pawnId)?.progressionState;
            if (progression != null)
            {
                // The submitted boundary uses this same synchronous tick. Storing it directly also
                // keeps observation truthful if page policy disables the arrival or its adapter faults;
                // anniversary state must not depend on whether a page was allowed to commit.
                progression.arrivalAnniversaryStartTick = boundaryTick;
                progression.arrivalAnniversaryBoundaryResolved = true;
                progression.lastArrivalAnniversaryYear = 0;

                // A cursor at the wipe boundary prevents the legacy migration and future discovery scan
                // from reconstructing deaths the pawn has forgotten. Reset the day guard so a genuinely
                // post-wipe loss can still be written on the same in-game day.
                progression.bondedDeathMemories?.Clear();
                progression.lastBondedDeathDiscoveryTick = boundaryTick;
                progression.bondedDeathHistoryMigrationComplete = true;
                progression.lastBondedDeathPageDay = int.MinValue;

                // Active mech rows keep a service clock that later decides whether a loss is worth a
                // page, so restart that clock here. Completed rows remain as inert loss deduplication.
                // A called-but-undefeated boss likewise retains delayed ownership and could otherwise
                // emit its terminal page after the wipe; completed boss rows stay historical dedup.
                MechanitorObservationState mechanitor =
                    progression.biotechProgressionState?.mechanitorObservation;
                MechanitorLifecyclePolicy.RebaselineActiveMechsAfterMemoryReset(
                    mechanitor?.observedMechs,
                    boundaryTick);
                mechanitor?.bossCalls?.RemoveAll(
                    call => call == null || !call.defeatedObserved);

                // Deathrest lifetime and cooldown are autobiography pacing rather than physical pawn
                // state. Restart them without reading the DLC tracker; the saved primitive row exists
                // safely in a no-DLC game and the next genuine interruption may become a new memory.
                DeathrestInterruptionPolicy.ForgetMemory(
                    progression.biotechProgressionState?.deathrestObservation);
            }

            // Royal bestowing offers have no deadline. Retire only rows owned by this exact heir, or a
            // pre-wipe inheritance could later claim/suppress an unrelated post-wipe title transition.
            // The saved row is DLC-independent primitive state; no Royalty tracker is read here.
            royaltyPendingSuccessions?.RemoveAll(succession =>
                RoyalSuccessionPolicy.IsOwnedByHeir(succession?.heirPawnId, pawnId));

            // A live persona bond is real physical ownership, so keep the weapon ID, pawn, epoch,
            // traits, and consumed first-kill flags. Only its autobiographical separation/formation
            // timeline restarts, preventing a pending row from separating immediately or a separated
            // row from producing a false recovery page on the next primary observation.
            for (int i = 0; i < (royaltyPersonaBonds?.Count ?? 0); i++)
            {
                PersonaBondState row = royaltyPersonaBonds[i];
                PersonaBondStateSnapshot reset = PersonaLifecyclePolicy.RebaselineAfterMemoryReset(
                    row?.ToSnapshot(),
                    pawnId,
                    boundaryTick);
                if (reset != null)
                {
                    royaltyPersonaBonds[i] = PersonaBondState.FromSnapshot(reset);
                }
            }
        }
    }
}

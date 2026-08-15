// Shared-arc side of the pawn memory-reset boundary. These stores describe world events involving
// several pawns, so Brainwipe projects away only the wiped pawn's autobiographical evidence instead
// of deleting the whole arc and damaging the other participants' truthful continuity.
using PawnDiary.Capture;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>Applies exact-pawn memory boundaries to every shared saved arc.</summary>
        private void ResetSharedArcMemoryForPawn(string pawnId, int memoryBoundaryTick)
        {
            // These detached stores are intentionally processed without DLC ownership gates. A save can
            // later be loaded with different DLCs active; dormant pre-wipe autobiography must stay inert.
            BiotechFamilyMemoryResetPolicy.ResetForPawn(
                biotechFamilyArcs,
                pawnId,
                memoryBoundaryTick);
            ExcludeOdysseyWriterFromActiveJourney(pawnId);

            // A non-terminal CreepJoiner row can suppress every later surgical disclosure solely because
            // the pawn saw one before Brainwipe. Drop that autobiographical row; terminal world outcomes
            // remain durable replay barriers and cannot be re-authored after a reset.
            AnomalyPersistentStateSnapshot anomalyState = AnomalyStateSnapshot();
            anomalyState.creepJoinerArcs =
                CreepJoinerMemoryResetPolicy.RemoveNonterminalForPawn(
                    anomalyState.creepJoinerArcs,
                    pawnId);
            ApplyAnomalyState(anomalyState);
        }
    }
}

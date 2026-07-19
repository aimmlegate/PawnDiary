// Runtime owner for successful dramatic Royalty permit pages. The Harmony adapter hands this class
// only a live eligible Pawn plus a detached snapshot; pure policy recognizes the source, the existing
// RaidSignal owner claims matching quick aid, and the ordinary ingestion bus persists the page.
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>Arbitrates one exact successful permit use and submits its detached event.</summary>
        internal void ObserveRoyalPermitUse(Pawn pawn, RoyalPermitUseSnapshot use)
        {
            if (!ModsConfig.RoyaltyActive || pawn == null || use == null) return;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            string eventDefName = RoyalPermitPolicy.EventDefNameForFamily(use.permitFamilyToken);
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRoyalPermit(eventDefName);
            bool outputEnabled = group != null && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
            RoyalPermitDecision decision = RoyalPermitPolicy.Decide(
                use, policy.enabled, outputEnabled);
            if (!decision.recognized) return;

            // Ownership is source truth, not output truth. A disabled permit group must not leak a
            // second generic RaidFriendly story for the same action.
            if (decision.familyToken == RoyalPermitFamilyTokens.MilitaryAid)
                QuickMilitaryAidRaidCorrelation.Claim(use, use.tick, policy);
            DiaryEvents.Submit(new RoyalPermitSignal(pawn, use, policy, group));
        }
    }
}

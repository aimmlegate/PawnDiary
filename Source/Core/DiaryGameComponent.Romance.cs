// Romance events — the Pawn_RelationsTracker.AddDirectRelation hook's diary flow. This is the
// FIRST source designed from scratch onto the Event Catalog (every other source migrated an
// existing RecordX method). When a colonist gains a romance relation (Lover / Spouse / ExLover /
// ExSpouse) with another colonist, this records a pairwise DiaryEvent with both POV entries so
// each pawn gets their own first-person take on the relationship change.
//
// The event carries a "romance=" game-context marker so the UI classifies it into the Romance
// domain. The "kind=" field (married / lover / divorce / breakup) lets prompt policy discriminate
// weddings from breakups without re-parsing the relation defName.
//
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Records one romance-relation change as a pairwise diary event. Called by the
        /// <see cref="PawnRelationAddPatch"/> Harmony postfix on
        /// <c>Pawn_RelationsTracker.AddDirectRelation</c>.
        /// </summary>
        public void RecordRomance(Pawn pawn, Pawn otherPawn, PawnRelationDef relationDef)
        {
            if (!CanRecordGameplayEventNow()
                || pawn == null
                || otherPawn == null
                || relationDef == null
                || PawnDiaryMod.Settings == null)
            {
                return;
            }

            // XML owns which relation defs count as diary-worthy romance milestones. This keeps
            // modded relation compatibility data-only instead of hardcoding defName lists in C#.
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRomanceRelation(relationDef.defName);
            if (group == null)
            {
                return;
            }

            // Snapshot live facts for the catalog. The pair dedup below uses a canonical key so the
            // mirrored AddDirectRelation call (if RimWorld adds the relation symmetrically on the
            // other pawn's tracker) collapses to one diary event.
            string firstPawnId = pawn.GetUniqueLoadID();
            string otherPawnId = otherPawn.GetUniqueLoadID();
            bool firstEligible = IsDiaryEligible(pawn);
            bool secondEligible = IsDiaryEligible(otherPawn);

            RomanceEventData data = new RomanceEventData
            {
                PawnId = firstPawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = relationDef.defName,
                FirstPawnId = firstPawnId,
                SecondPawnId = otherPawnId,
                FirstEligible = firstEligible,
                SecondEligible = secondEligible,
            };

            CaptureContext ctx = BuildCaptureContext(
                eligible: firstEligible && secondEligible,
                userEnabled: PawnDiaryMod.Settings.IsGroupEnabled(group.defName),
                signalEnabled: true,
                ambientSignalEnabled: true);

            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Romance);
            CaptureDecision decision = spec != null
                ? spec.Decide(data, ctx)
                : CaptureDecision.Drop;
            if (decision != CaptureDecision.GeneratePair)
            {
                return;
            }

            // Pair dedup collapses the mirrored AddDirectRelation call from the other participant.
            string pairDedupKey = "romance|" + PairKey(pawn, otherPawn) + "|" + relationDef.defName;
            if (RecentlyRecorded(recentRomanceEvents, pairDedupKey, DiaryTuning.Current.romanceDedupTicks))
            {
                return;
            }

            // Impure build: label, XML prompt instruction, localized text, gameContext.
            string label = relationDef.LabelCap.Resolve();
            string cleanedLabel = DiaryContextBuilder.CleanLine(label);
            string kind = RomanceEventData.KindFor(relationDef.defName);
            string gameContext = RomanceEventData.BuildGameContext(relationDef.defName, cleanedLabel, kind);
            string instruction = InteractionGroups.InstructionForGroup(group);

            // Both pawns see the same factual text; the LLM prompt adds per-pawn summaries and
            // continuity when generating each POV.
            string text = "PawnDiary.Event.Romance".Translate(pawn.LabelShortCap, cleanedLabel, otherPawn.LabelShortCap).Resolve();

            DiaryEvent romanceEvent = AddPairwiseEvent(pawn, otherPawn, relationDef.defName, label,
                text, text, instruction, gameContext);
            QueuePairwiseGeneration(romanceEvent);
        }
    }
}

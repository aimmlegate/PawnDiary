// Romance ingestion signal — the impure capture+emit half of the "romance relation changed" source
// (Pawn_RelationsTracker.AddDirectRelation, filtered to Lover/Spouse/ExLover/ExSpouse). Replaces the
// old romance recorder. This is a PAIR source: it records one DiaryEvent with both
// pawns' POV entries so each gets their own first-person take on the relationship change.
//
// The pure decision + game-context format live in Source/Capture/Events/RomanceEventData.cs.
// New to C#/RimWorld? See AGENTS.md.
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one romance-relation change and emits it as a pairwise diary event. Built by
    /// <see cref="PawnRelationAddPatch"/> and submitted via <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    internal sealed class RomanceSignal : DiarySignal
    {
        private readonly Pawn pawn;
        private readonly Pawn otherPawn;
        private readonly PawnRelationDef relationDef;
        private readonly DiaryInteractionGroupDef group;
        private readonly RomanceEventData payload;

        public RomanceSignal(Pawn pawn, Pawn otherPawn, PawnRelationDef relationDef)
        {
            this.pawn = pawn;
            this.otherPawn = otherPawn;
            this.relationDef = relationDef;

            // Same guard as the old romance recorder (GamePlaying first, before any Find.TickManager
            // access below): drop unless the game is playing and both pawns + the def + settings are
            // present. A null payload tells the dispatcher to drop.
            if (!DiaryGameComponent.GamePlaying || pawn == null || otherPawn == null
                || relationDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            // XML owns which relation defs count as diary-worthy romance milestones. If the relation
            // is not a configured romance group, drop (payload stays null).
            group = InteractionGroups.ClassifyRomanceRelation(relationDef.defName);
            if (group == null)
            {
                return;
            }

            payload = new RomanceEventData
            {
                PawnId = pawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = relationDef.defName,
                FirstPawnId = pawn.GetUniqueLoadID(),
                SecondPawnId = otherPawn.GetUniqueLoadID(),
                FirstEligible = DiaryGameComponent.IsDiaryEligible(pawn),
                SecondEligible = DiaryGameComponent.IsDiaryEligible(otherPawn),
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: payload.FirstEligible && payload.SecondEligible,
                userEnabled: PawnDiaryMod.Settings.IsGroupEnabled(group.defName),
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload != null ? payload.DedupKey() : string.Empty;

        public override int DedupWindowTicks => DiaryTuning.Current.romanceDedupTicks;

        public override void CaptureKnowledgeWithoutPage(DiaryGameComponent sink)
        {
            if (payload == null)
            {
                return;
            }

            sink.CaptureEventKnowledgeWithoutPage(
                pawn, otherPawn, relationDef.defName, BuildKnowledgeContext(), payload.Tick);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GeneratePair)
            {
                return;
            }

            // Impure build: label, XML prompt instruction, localized text, gameContext.
            string label = relationDef.LabelCap.Resolve();
            string cleanedLabel = DiaryLineCleaner.CleanLine(label);
            string gameContext = BuildKnowledgeContext();
            string instruction = InteractionGroups.InstructionForGroup(group);

            // Both pawns see the same factual text; the LLM prompt adds per-pawn summaries and
            // continuity when generating each POV.
            string text = "PawnDiary.Event.Romance".Translate(pawn.LabelShortCap, cleanedLabel, otherPawn.LabelShortCap).Resolve();

            DiaryEvent romanceEvent = sink.AddPairwiseEvent(pawn, otherPawn, relationDef.defName, label,
                text, text, instruction, gameContext);
            sink.QueuePair(romanceEvent);
        }

        private string BuildKnowledgeContext()
        {
            string cleanedLabel = DiaryLineCleaner.CleanLine(relationDef.LabelCap.Resolve());
            string kind = RomanceEventData.KindFor(relationDef.defName);
            return RomanceEventData.BuildGameContext(relationDef.defName, cleanedLabel, kind);
        }
    }
}

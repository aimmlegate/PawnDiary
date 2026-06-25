// Mental states — the MentalStateHandler.TryStartMentalState hook's diary flow, now built on the
// Event Catalog pattern (see Source/Capture/). This is the FIRST migrated source whose Decider can
// return GeneratePair: a social fight (MentalStateDef "SocialFighting") between two eligible pawns
// becomes a pairwise DiaryEvent with both POV entries (the mirrored second call from the other
// participant is deduped), while every other accepted break becomes a solo event from the breaking
// pawn's POV.
//
// This file holds the IMPURE half of the flow: snapshot live Pawn/MentalStateDef facts into
// MentalStateEventData + CaptureContext, ask DiaryEventCatalog.Get(MentalState) for the decision,
// then perform the per-decision impure side-effects (pair dedup with canonical pair key, solo
// dedup with pawn+defName key, build label/text/instruction via RimWorld translation, AddPairwise
// /AddSoloEvent, queue LLM). The pure pair-vs-solo decision and the pure game-context string
// assembly live in Source/Capture/Events/MentalStateEventData.cs.
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
        /// Records one started mental state as either a pairwise social-fight event or a solo
        /// mental-break event. Delegates the "pair vs solo vs drop?" decision to
        /// MentalStateEventData.Decide (pure) and keeps the impure side-effects here.
        /// </summary>
        public void RecordMentalState(Pawn pawn, MentalStateDef stateDef, Pawn otherPawn, string reason)
        {
            if (!CanRecordGameplayEventNow() || pawn == null || stateDef == null)
            {
                return;
            }

            // Snapshot live facts the pure Decider needs. OtherPawnId/OtherPawnLabel are set whenever
            // otherPawn is non-null (the solo break can target any pawn, eligible or not); eligibility
            // is a separate flag feeding only the pair decision.
            string pawnId = pawn.GetUniqueLoadID();
            bool hasOtherPawn = otherPawn != null;
            string otherPawnId = hasOtherPawn ? otherPawn.GetUniqueLoadID() : null;
            string otherPawnLabel = hasOtherPawn
                ? DiaryLineCleaner.CleanLine(otherPawn.LabelShortCap)
                : null;

            MentalStateEventData data = new MentalStateEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = stateDef.defName,
                OtherPawnId = otherPawnId,
                OtherPawnEligible = hasOtherPawn && IsDiaryEligible(otherPawn) && otherPawn != pawn,
                OtherPawnLabel = otherPawnLabel,
            };

            CaptureContext ctx = BuildCaptureContext(
                eligible: IsDiaryEligible(pawn),
                userEnabled: PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.IsMentalStateEnabled(stateDef),
                signalEnabled: true,
                ambientSignalEnabled: true);

            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.MentalState);
            CaptureDecision decision = spec != null
                ? spec.Decide(data, ctx)
                : CaptureDecision.Drop;
            if (decision == CaptureDecision.Drop)
            {
                return;
            }

            // Impure build: label, instruction, cleaned reason — all need RimWorld translation.
            string label = stateDef.LabelCap.Resolve();
            string instruction = InteractionGroups.InstructionForMentalState(stateDef);
            string cleanedLabel = DiaryLineCleaner.CleanLine(label);
            string cleanedReason = DiaryLineCleaner.CleanLine(reason);

            if (decision == CaptureDecision.GeneratePair)
            {
                // Pair dedup collapses the mirrored SocialFighting call from the other participant;
                // PairKey canonicalizes the two ids so both calls hit the same key.
                string pairDedupKey = "fight|" + PairKey(pawn, otherPawn);
                if (RecentlyRecorded(recentMentalEvents, pairDedupKey, DiaryTuning.Current.socialFightDedupTicks))
                {
                    return;
                }

                string text = "PawnDiary.Event.SocialFight".Translate(pawn.LabelShortCap, otherPawn.LabelShortCap);
                string gameContext = MentalStateEventData.BuildPairGameContext(
                    stateDef.defName, cleanedLabel, cleanedReason);

                DiaryEvent fightEvent = AddPairwiseEvent(pawn, otherPawn, stateDef.defName, label,
                    text, text, instruction, gameContext);
                QueuePairwiseGeneration(fightEvent);
                return;
            }

            // Solo break: separate dedup window per pawn+defName.
            string soloDedupKey = "break|" + pawnId + "|" + stateDef.defName;
            if (RecentlyRecorded(recentMentalEvents, soloDedupKey, DiaryTuning.Current.mentalBreakDedupTicks))
            {
                return;
            }

            string breakText = BuildMentalBreakText(pawn, label, otherPawn, reason);
            string breakContext = MentalStateEventData.BuildSoloGameContext(
                stateDef.defName, cleanedLabel, otherPawnLabel, cleanedReason);

            DiaryEvent breakEvent = AddSoloEvent(pawn, otherPawn, stateDef.defName, label,
                breakText, instruction, breakContext);
            QueueLlmRewrite(breakEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Assembles a human-readable fallback description for a mental break, including target and
        /// reason if available. Stays impure (Translate calls).
        /// </summary>
        private static string BuildMentalBreakText(Pawn pawn, string label, Pawn otherPawn, string reason)
        {
            string text = "PawnDiary.Event.MentalBreak".Translate(pawn.LabelShortCap, DiaryLineCleaner.CleanLine(label));
            if (otherPawn != null)
            {
                text += "PawnDiary.Event.DirectedAt".Translate(DiaryLineCleaner.CleanLine(otherPawn.LabelShortCap)).Resolve();
            }

            string cleanReason = DiaryLineCleaner.CleanLine(reason);
            if (!string.IsNullOrWhiteSpace(cleanReason))
            {
                text += "PawnDiary.Event.ReasonSuffix".Translate(cleanReason).Resolve();
            }

            return text + ".";
        }
    }
}

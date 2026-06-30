// Caravan-journey ingestion signal — the impure capture+emit half of caravan departure/arrival
// hooks. The event fans out to eligible colonists traveling with the player caravan, capped by XML
// policy to keep API bursts bounded.
using System;
using System.Collections.Generic;
using System.Text;
using PawnDiary.Capture;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Player caravan departure/arrival fan-out.
    /// </summary>
    public sealed class CaravanJourneyFanoutSignal : DiaryFanoutSignal
    {
        private readonly bool valid;
        private readonly string colonyDedupKey;
        private readonly string signal;
        private readonly string label;
        private readonly string routeLabel;
        private readonly string memberSummary;
        private readonly int pawnCount;
        private readonly int animalCount;
        private readonly List<Pawn> pawns = new List<Pawn>();
        private readonly DiaryInteractionGroupDef group;

        public CaravanJourneyFanoutSignal(Caravan caravan, string signal, string routeLabel)
        {
            if (!DiaryGameComponent.GamePlaying || caravan == null || !caravan.IsPlayerControlled || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!CaravanJourneyEventData.IsKnownSignal(signal))
            {
                return;
            }

            DiaryInteractionGroupDef classified = InteractionGroups.ClassifyCaravanJourney(signal);
            if (classified == null || !PawnDiaryMod.Settings.IsGroupEnabled(classified.defName))
            {
                return;
            }

            group = classified;
            this.signal = signal;
            label = LabelFor(signal);
            this.routeLabel = DiaryLineCleaner.CleanLine(routeLabel);

            int humans;
            int animals;
            memberSummary = SnapshotCaravanPawns(caravan.PawnsListForReading, pawns, out humans, out animals);
            pawnCount = humans;
            animalCount = animals;
            if (pawns.Count == 0)
            {
                return;
            }

            colonyDedupKey = "caravan_journey|" + signal + "|" + Find.TickManager.TicksGame + "|" + MemberKey(pawns);
            valid = true;
        }

        public override string ColonyDedupKey => valid ? colonyDedupKey : string.Empty;

        public override int ColonyDedupTicks => DiarySignalPolicies.CaravanJourneyDedupTicks;

        public override IEnumerable<DiarySignal> PerPawnSignals()
        {
            if (!valid)
            {
                yield break;
            }

            int cap = DiarySignalPolicies.CaravanJourneyMaxFanoutPawns;
            int emitted = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || !DiaryGameComponent.IsDiaryEligible(pawn))
                {
                    continue;
                }

                yield return new CaravanJourneyPawnSignal(this, pawn, pawn.GetUniqueLoadID());
                emitted++;
                if (cap > 0 && emitted >= cap)
                {
                    yield break;
                }
            }
        }

        private static string SnapshotCaravanPawns(
            List<Pawn> caravanPawns,
            List<Pawn> eligiblePawns,
            out int humanlikePawnCount,
            out int animalCount)
        {
            humanlikePawnCount = 0;
            animalCount = 0;
            if (caravanPawns == null)
            {
                return string.Empty;
            }

            StringBuilder names = new StringBuilder();
            for (int i = 0; i < caravanPawns.Count; i++)
            {
                Pawn pawn = caravanPawns[i];
                if (pawn == null)
                {
                    continue;
                }

                if (pawn.RaceProps != null && pawn.RaceProps.Humanlike)
                {
                    humanlikePawnCount++;
                    if (DiaryGameComponent.IsDiaryEligible(pawn))
                    {
                        eligiblePawns.Add(pawn);
                        if (names.Length > 0)
                        {
                            names.Append(", ");
                        }

                        names.Append(DiaryLineCleaner.CleanLine(pawn.LabelShortCap));
                    }
                }
                else
                {
                    animalCount++;
                }
            }

            return names.ToString();
        }

        private static string LabelFor(string signal)
        {
            return string.Equals(signal, CaravanJourneyEventData.Arrived, StringComparison.OrdinalIgnoreCase)
                ? "PawnDiary.Event.CaravanJourney.Arrived.Label".Translate().Resolve()
                : "PawnDiary.Event.CaravanJourney.Departed.Label".Translate().Resolve();
        }

        private static string MemberKey(List<Pawn> members)
        {
            StringBuilder key = new StringBuilder();
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] == null)
                {
                    continue;
                }

                if (key.Length > 0)
                {
                    key.Append("|");
                }

                key.Append(members[i].GetUniqueLoadID());
            }

            return key.ToString();
        }

        private sealed class CaravanJourneyPawnSignal : DiarySignal
        {
            private readonly CaravanJourneyFanoutSignal source;
            private readonly Pawn pawn;
            private readonly CaravanJourneyEventData payload;

            public CaravanJourneyPawnSignal(CaravanJourneyFanoutSignal source, Pawn pawn, string pawnId)
            {
                this.source = source;
                this.pawn = pawn;
                payload = new CaravanJourneyEventData
                {
                    PawnId = pawnId,
                    Tick = Find.TickManager.TicksGame,
                    Signal = source.signal,
                    CaravanLabel = source.label,
                    RouteLabel = source.routeLabel,
                    MemberSummary = source.memberSummary,
                    PawnCount = source.pawnCount,
                    AnimalCount = source.animalCount
                };
            }

            public override DiaryEventData Payload => payload;

            public override CaptureContext BuildContext()
            {
                return DiaryGameComponent.BuildCaptureContext(
                    eligible: true,
                    userEnabled: true,
                    signalEnabled: DiarySignalPolicies.Enabled(DiarySignalPolicies.CaravanJourney),
                    ambientSignalEnabled: true);
            }

            public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
            {
                if (decision != CaptureDecision.GenerateSolo)
                {
                    return;
                }

                string context = CaravanJourneyEventData.BuildGameContext(
                    source.signal, source.label, source.routeLabel, source.memberSummary,
                    source.pawnCount, source.animalCount);
                string textKey = string.Equals(source.signal, CaravanJourneyEventData.Arrived, StringComparison.OrdinalIgnoreCase)
                    ? "PawnDiary.Event.CaravanJourney.Arrived"
                    : "PawnDiary.Event.CaravanJourney.Departed";
                string text = textKey.Translate(pawn.LabelShortCap, source.routeLabel, source.memberSummary).Resolve();
                string instruction = InteractionGroups.InstructionForGroup(source.group);

                DiaryEvent diaryEvent = sink.AddSoloEvent(
                    pawn, null, source.signal, source.label, text, instruction, context);
                if (diaryEvent == null)
                {
                    return;
                }

                sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
            }
        }
    }
}

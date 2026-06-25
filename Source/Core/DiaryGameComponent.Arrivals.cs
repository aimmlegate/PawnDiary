// The colony-arrival flow: every colonist gets a neutral first diary entry describing how they
// joined (game start vs. recruited/joined later). Founding colonists are scanned once on the first
// tick that has maps (StartedNewGame runs before maps exist); pawns who join later are recorded by
// the Pawn.SetFaction Harmony patch, which calls RecordColonistArrival directly. These build the
// "arrival_*" game-context string the neutral arrival prompt reads.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// New-game bootstrap: records one neutral arrival entry for each starting colonist once
        /// RimWorld has finished creating maps and free-colonist lists.
        /// </summary>
        private bool TryRecordStartingColonistArrivals()
        {
            if (!CanRecordGameplayEventNow())
            {
                return false;
            }

            if (Find.Maps == null || Find.Maps.Count == 0)
            {
                return false;
            }

            for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
            {
                Map map = Find.Maps[mapIndex];
                if (map?.mapPawns?.FreeColonists == null)
                {
                    continue;
                }

                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int i = 0; i < colonists.Count; i++)
                {
                    RecordColonistArrival(colonists[i], BuildStartingArrivalContext());
                }
            }

            return true;
        }

        /// <summary>
        /// Records the first neutral entry for a pawn: how they became part of the colony. This is
        /// public because the Pawn.SetFaction Harmony patch calls it after vanilla confirms the join.
        /// </summary>
        public void RecordColonistArrival(Pawn pawn, string arrivalContext)
        {
            if (!CanRecordGameplayEventNow() || pawn == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            DiaryInteractionGroupDef arrivalGroup = InteractionGroups.ByKey(ArrivalGroupKey);
            ArrivalEventData data = new ArrivalEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = ArrivalDefName,
                PawnLabel = DiaryLineCleaner.CleanLine(pawn.LabelShortCap),
                PawnLoadId = pawnId,
                ArrivalContext = arrivalContext,
                HasExistingArrival = HasArrivalEventFor(pawnId),
            };
            CaptureContext ctx = BuildCaptureContext(
                eligible: IsDiaryEligible(pawn),
                userEnabled: arrivalGroup == null || PawnDiaryMod.Settings.IsGroupEnabled(arrivalGroup.defName),
                signalEnabled: true,
                ambientSignalEnabled: true);

            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Arrival);
            CaptureDecision decision = spec != null
                ? spec.Decide(data, ctx)
                : CaptureDecision.Drop;
            if (decision != CaptureDecision.GenerateSoloArrivalDescription)
            {
                return;
            }

            bool startingPawn = ArrivalEventData.IsStartingArrival(arrivalContext);
            string label = "PawnDiary.Event.ArrivalLabel".Translate().Resolve();
            string text = startingPawn
                ? "PawnDiary.Event.StartingArrival".Translate(pawn.LabelShortCap).Resolve()
                : "PawnDiary.Event.JoinedArrival".Translate(pawn.LabelShortCap).Resolve();

            DiaryEvent arrivalEvent = AddSoloEvent(pawn, null, ArrivalDefName, label, text, string.Empty,
                ArrivalEventData.BuildGameContext(data.PawnLabel, data.PawnLoadId, data.ArrivalContext));
            QueueArrivalDescription(arrivalEvent);
        }

        private bool HasArrivalEventFor(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || diaryEvents == null)
            {
                return false;
            }

            for (int i = 0; i < diaryEvents.Count; i++)
            {
                if (diaryEvents[i] != null && diaryEvents[i].IsArrivalDescriptionFor(pawnId))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildStartingArrivalContext()
        {
            List<string> parts = new List<string>
            {
                "arrival_source=game_start"
            };

            Scenario scenario = Verse.Current.Game?.Scenario;
            if (scenario != null)
            {
                string scenarioName = DiaryLineCleaner.CleanLine(scenario.name);
                if (!string.IsNullOrWhiteSpace(scenarioName))
                {
                    parts.Add("scenario_name=" + scenarioName);
                }

                string scenarioDescription = DiaryLineCleaner.CleanLine(scenario.description);
                if (!string.IsNullOrWhiteSpace(scenarioDescription))
                {
                    parts.Add("scenario_description=" + scenarioDescription);
                }
            }

            return string.Join("; ", parts.ToArray());
        }
    }
}

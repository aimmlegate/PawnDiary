// The colony-arrival flow: every colonist gets a neutral first diary entry describing how they
// joined (game start vs. recruited/joined later). Founding colonists are scanned once on the first
// tick that has maps (StartedNewGame runs before maps exist); pawns who join later are recorded by
// the Pawn.SetFaction Harmony patch, which calls RecordColonistArrival directly. These build the
// "arrival_*" game-context string the neutral arrival prompt reads.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
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
                    DiaryEvents.Submit(new ArrivalSignal(colonists[i], BuildStartingArrivalContext()));
                }
            }

            return true;
        }

        // internal: the ArrivalSignal capture reads this through DiaryGameComponent.Current to drop a
        // duplicate arrival page (the pawn already has one).
        internal bool HasArrivalEventFor(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            IReadOnlyList<DiaryEvent> allEvents = events.AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                if (allEvents[i] != null && allEvents[i].IsArrivalDescriptionFor(pawnId))
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
                string scenarioName = PromptTextSanitizer.LocalizedPromptText(scenario.name);
                if (!string.IsNullOrWhiteSpace(scenarioName))
                {
                    parts.Add("scenario_name=" + scenarioName);
                }

                string scenarioDescription = PromptTextSanitizer.LocalizedPromptText(scenario.description);
                if (!string.IsNullOrWhiteSpace(scenarioDescription))
                {
                    parts.Add("scenario_description=" + scenarioDescription);
                }
            }

            return string.Join("; ", parts.ToArray());
        }
    }
}

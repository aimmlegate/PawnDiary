// Raid events — the IncidentWorker_Raid hook's diary flow, built on the Event Catalog pattern
// (see Source/Capture/). A raid or infestation is recorded as a separate solo DiaryEvent for each
// eligible colonist on the target map, because each survivor experiences the threat independently.
// The event carries a "raid=" game-context marker so the UI classifies it into the Raid domain.
//
// This is a MINIMAL realization by design (see CHANGELOG): only the incident defName, the raider
// faction defName, raid points, arrival mode, and strategy are captured. The latter two stay as
// string defNames so prompt policy can distinguish walk-in, drop-pod, and infestation danger
// without referencing paid-DLC content or live RimWorld objects in pure code.
//
// This file holds both halves of the multi-pawn fan-out (mirroring MoodEvents.cs):
// (1) per-source-call gates that stay impure and outside the catalog — CanRecordGameplayEventNow,
//     the user-toggle gate, the raid-level dedup (peek + conditional mark);
// (2) the per-pawn catalog dispatch loop — for each eligible colonist on the target map, build a
//     RaidEventData + CaptureContext, ask the catalog for a decision, and if GenerateSolo build
//     the entry via the pure RaidEventData.BuildGameContext helper and the localized event text.
//
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Globalization;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Records a raid as solo diary events for each eligible colonist on the raid's target map.
        /// Called by the <see cref="RaidExecutePatch"/> Harmony postfix on
        /// <c>IncidentWorker.TryExecute</c> (filtered to raid subclasses).
        /// </summary>
        public void RecordRaid(IncidentParms parms, IncidentDef incidentDef)
        {
            if (!CanRecordGameplayEventNow()
                || parms == null
                || incidentDef == null
                || PawnDiaryMod.Settings == null)
            {
                return;
            }

            string incidentDefName = incidentDef.defName;
            if (string.IsNullOrEmpty(incidentDefName))
            {
                return;
            }

            string arrivalModeDefName = parms.raidArrivalMode?.defName;
            string strategyDefName = parms.raidStrategy?.defName;
            string raidClassifierKey = RaidClassifierKey(incidentDefName, arrivalModeDefName, strategyDefName);

            // XML owns which raid incidents count as diary-worthy. The Raid catch-all ("Raids")
            // matches everything not claimed by friendly/drop-pod/infestation subsets.
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRaid(raidClassifierKey);
            if (group == null || !PawnDiaryMod.Settings.IsGroupEnabled(group.defName))
            {
                return;
            }

            // Raids target a single map (IncidentParms.target is an IIncidentTarget; Map implements
            // it). Caravan/world raids have no FreeColonists list to fan out to, so we no-op them.
            Map map = parms.target as Map;
            if (map == null || map.mapPawns == null)
            {
                return;
            }

            // Minimal payload facts: raider faction defName + raid points + arrival/strategy defNames.
            // Faction is always present on a raid IncidentParms; "unknown" is the schema sentinel.
            string raiderFactionDefName = parms.faction?.def?.defName;
            if (string.IsNullOrEmpty(raiderFactionDefName))
            {
                raiderFactionDefName = RaidEventData.FactionUnknown;
            }

            // Points are a float in RimWorld (threat budget); the brief asks for an int, so round.
            int points = Math.Max(0, (int)Math.Round(parms.points));
            string pointsText = points.ToString(CultureInfo.InvariantCulture);

            // Incident label for the context marker. IncidentDef.label is usually set ("enemy raid",
            // "friendly raid"); fall back to the defName when the def ships no label.
            string rawLabel = incidentDef.LabelCap.Resolve();
            string cleanedLabel = !string.IsNullOrWhiteSpace(rawLabel)
                ? DiaryLineCleaner.CleanLine(rawLabel)
                : incidentDefName;

            // Colony-level dedup: one window per raid, keyed by incident/map/faction/points. Only
            // mark the key after at least one pawn records an entry, so an empty colonist list
            // cannot consume the whole window (same shape as RecordMoodEvent).
            string dedupKey = "raid|" + incidentDefName + "|" + map.Index + "|" + raiderFactionDefName + "|" + pointsText;
            if (IsRecentlyRecorded(recentRaidEvents, dedupKey, DiaryTuning.Current.raidDedupTicks))
            {
                return;
            }

            string instruction = InteractionGroups.InstructionForGroup(group);
            bool delayGeneration = RaidEventData.ShouldDelayGeneration(
                incidentDefName,
                arrivalModeDefName,
                strategyDefName,
                DiaryTuning.Current.raidGenerationDelayTicks);
            int generationReadyTick = Find.TickManager.TicksGame
                + Math.Max(0, DiaryTuning.Current.raidGenerationDelayTicks);

            // Look up the catalog spec once per RecordRaid call. If somehow missing (a future source
            // forgot to Register), the per-pawn loop falls back to Drop and silently no-ops.
            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Raid);

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            HashSet<string> recordedPawnIds = new HashSet<string>();
            bool recordedAny = false;

            for (int j = 0; j < colonists.Count; j++)
            {
                Pawn pawn = colonists[j];
                if (pawn == null || !IsDiaryEligible(pawn))
                {
                    continue;
                }

                // A pawn could appear more than once during transitions; skip duplicates.
                string pawnId = pawn.GetUniqueLoadID();
                if (recordedPawnIds.Contains(pawnId))
                {
                    continue;
                }
                recordedPawnIds.Add(pawnId);

                // Each survivor gets their own solo diary event. The shared raid facts are copied
                // onto the payload so the pure Decider is self-contained.
                RaidEventData data = new RaidEventData
                {
                    PawnId = pawnId,
                    Tick = Find.TickManager.TicksGame,
                    DefName = incidentDefName,
                    Label = cleanedLabel,
                    FactionDefName = raiderFactionDefName,
                    Points = pointsText,
                    ArrivalModeDefName = arrivalModeDefName,
                    StrategyDefName = strategyDefName,
                };
                CaptureContext ctx = BuildCaptureContext(
                    eligible: true,
                    userEnabled: true,
                    signalEnabled: true,
                    ambientSignalEnabled: true);

                CaptureDecision decision = spec != null
                    ? spec.Decide(data, ctx)
                    : CaptureDecision.Drop;
                if (decision != CaptureDecision.GenerateSolo)
                {
                    continue;
                }

                string perPawnContext = RaidEventData.BuildGameContext(
                    incidentDefName, cleanedLabel, raiderFactionDefName, pointsText,
                    arrivalModeDefName, strategyDefName);

                string text = "PawnDiary.Event.Raid".Translate(pawn.LabelShortCap).Resolve();

                DiaryEvent raidEvent = AddSoloEvent(pawn, null, incidentDefName, cleanedLabel,
                    text, instruction, perPawnContext);
                if (raidEvent == null)
                {
                    continue;
                }

                recordedAny = true;
                if (delayGeneration)
                {
                    DelayGenerationUntil(raidEvent, DiaryEvent.InitiatorRole, generationReadyTick);
                }
                else
                {
                    QueueLlmRewrite(raidEvent, DiaryEvent.InitiatorRole);
                }
            }

            if (recordedAny)
            {
                MarkRecentlyRecorded(recentRaidEvents, dedupKey, DiaryTuning.Current.raidDedupTicks);
            }
        }

        /// <summary>
        /// Builds the string classifier that XML groups match. It starts with the incident defName so
        /// old exact/token matchers still see the source, then adds arrival/strategy defNames when
        /// RimWorld supplied them.
        /// </summary>
        private static string RaidClassifierKey(string incidentDefName, string arrivalModeDefName, string strategyDefName)
        {
            List<string> parts = new List<string> { incidentDefName };
            if (!string.IsNullOrWhiteSpace(arrivalModeDefName))
            {
                parts.Add("arrival=" + arrivalModeDefName);
            }

            if (!string.IsNullOrWhiteSpace(strategyDefName))
            {
                parts.Add("strategy=" + strategyDefName);
            }

            return string.Join("; ", parts.ToArray());
        }

    }
}

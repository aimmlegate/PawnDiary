// Raid ingestion signal — the impure capture+emit half of the "colony raid" source
// (IncidentWorker.TryExecute, filtered to raid-like incidents). Replaces the old
// the old raid recorder. A raid is a colony-wide FAN-OUT: one solo entry per eligible
// colonist on the target map, with a single colony-level dedup window (peek before the loop, mark
// only after at least one entry emits — handled by the shared DiaryFanoutSignal dispatch).
//
// The raid-level facts (incident defName, raider faction, points, arrival mode, strategy) are
// computed once in the fan-out constructor and copied onto each per-pawn payload, so the pure
// per-pawn Decide stays self-contained. Pure decision + game-context + delay policy live in
// Source/Capture/Events/RaidEventData.cs. New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Colony-wide raid fan-out. Built by <see cref="RaidExecutePatch"/> and submitted via
    /// <see cref="DiaryEvents.Submit(DiaryFanoutSignal)"/>. All raid-level gates (group classification +
    /// user toggle, valid target map) run in the constructor; if any fails the fan-out yields nothing.
    /// </summary>
    public sealed class RaidFanoutSignal : DiaryFanoutSignal
    {
        private readonly bool valid;
        private readonly Map map;
        private readonly string colonyDedupKey;

        // Shared raid facts copied onto each per-pawn payload + read by each child's Emit.
        internal string IncidentDefName { get; }
        internal string CleanedLabel { get; }
        internal string RaiderFactionDefName { get; }
        internal string PointsText { get; }
        internal string ArrivalModeDefName { get; }
        internal string StrategyDefName { get; }
        internal string Instruction { get; }
        internal bool DelayGeneration { get; }
        internal int GenerationReadyTick { get; }

        public RaidFanoutSignal(IncidentParms parms, IncidentDef incidentDef)
        {
            // GamePlaying first (before any Find.TickManager access), mirroring the old raid recorder's
            // leading CanRecordGameplayEventNow guard.
            if (!DiaryGameComponent.GamePlaying || parms == null || incidentDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            IncidentDefName = incidentDef.defName;
            if (string.IsNullOrEmpty(IncidentDefName))
            {
                return;
            }

            ArrivalModeDefName = parms.raidArrivalMode?.defName;
            StrategyDefName = parms.raidStrategy?.defName;

            // XML owns which raid incidents count as diary-worthy. The Raid catch-all ("Raids") matches
            // everything not claimed by friendly/drop-pod/infestation subsets.
            string raidClassifierKey = RaidClassifierKey(IncidentDefName, ArrivalModeDefName, StrategyDefName);
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyRaid(raidClassifierKey);
            if (group == null || !PawnDiaryMod.Settings.IsGroupEnabled(group.defName))
            {
                return;
            }

            // Raids target a single map; caravan/world raids have no FreeColonists list to fan out to.
            map = parms.target as Map;
            if (map == null || map.mapPawns == null)
            {
                map = null;
                return;
            }

            RaiderFactionDefName = parms.faction?.def?.defName;
            if (string.IsNullOrEmpty(RaiderFactionDefName))
            {
                RaiderFactionDefName = RaidEventData.FactionUnknown;
            }

            int points = Math.Max(0, (int)Math.Round(parms.points));
            PointsText = points.ToString(CultureInfo.InvariantCulture);

            string rawLabel = incidentDef.LabelCap.Resolve();
            CleanedLabel = !string.IsNullOrWhiteSpace(rawLabel)
                ? DiaryLineCleaner.CleanLine(rawLabel)
                : IncidentDefName;

            Instruction = InteractionGroups.InstructionForGroup(group);
            DelayGeneration = RaidEventData.ShouldDelayGeneration(
                IncidentDefName, ArrivalModeDefName, StrategyDefName, DiaryTuning.Current.raidGenerationDelayTicks);
            GenerationReadyTick = Find.TickManager.TicksGame + Math.Max(0, DiaryTuning.Current.raidGenerationDelayTicks);

            colonyDedupKey = "raid|" + IncidentDefName + "|" + map.Index + "|" + RaiderFactionDefName + "|" + PointsText;
            valid = true;
        }

        public override string ColonyDedupKey => valid ? colonyDedupKey : string.Empty;

        public override int ColonyDedupTicks => DiaryTuning.Current.raidDedupTicks;

        public override IEnumerable<DiarySignal> PerPawnSignals()
        {
            if (!valid)
            {
                yield break;
            }

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            HashSet<string> seen = new HashSet<string>();
            for (int j = 0; j < colonists.Count; j++)
            {
                Pawn pawn = colonists[j];
                if (pawn == null || !DiaryGameComponent.IsDiaryEligible(pawn))
                {
                    continue;
                }

                // A pawn could appear more than once during transitions; skip duplicates.
                string pawnId = pawn.GetUniqueLoadID();
                if (!seen.Add(pawnId))
                {
                    continue;
                }

                yield return new RaidPawnSignal(this, pawn, pawnId);
            }
        }

        /// <summary>
        /// Builds the string classifier XML groups match: the incident defName first (so old
        /// exact/token matchers still see the source), then arrival/strategy defNames when present.
        /// Moved verbatim from the old DiaryGameComponent.RaidClassifierKey.
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

    /// <summary>
    /// One colonist's solo raid entry. Created by <see cref="RaidFanoutSignal.PerPawnSignals"/>; carries
    /// the shared raid facts via its parent fan-out so the pure per-pawn Decide is self-contained.
    /// </summary>
    internal sealed class RaidPawnSignal : DiarySignal
    {
        private readonly RaidFanoutSignal raid;
        private readonly Pawn pawn;
        private readonly RaidEventData payload;

        public RaidPawnSignal(RaidFanoutSignal raid, Pawn pawn, string pawnId)
        {
            this.raid = raid;
            this.pawn = pawn;
            payload = new RaidEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = raid.IncidentDefName,
                Label = raid.CleanedLabel,
                FactionDefName = raid.RaiderFactionDefName,
                Points = raid.PointsText,
                ArrivalModeDefName = raid.ArrivalModeDefName,
                StrategyDefName = raid.StrategyDefName,
            };
        }

        public override DiaryEventData Payload => payload;

        // Raid-level gates already passed in the fan-out constructor; each eligible colonist records.
        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: true, userEnabled: true, signalEnabled: true, ambientSignalEnabled: true);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            string perPawnContext = RaidEventData.BuildGameContext(
                raid.IncidentDefName, raid.CleanedLabel, raid.RaiderFactionDefName, raid.PointsText,
                raid.ArrivalModeDefName, raid.StrategyDefName);

            string text = "PawnDiary.Event.Raid".Translate(pawn.LabelShortCap).Resolve();

            DiaryEvent raidEvent = sink.AddSoloEvent(pawn, null, raid.IncidentDefName, raid.CleanedLabel,
                text, raid.Instruction, perPawnContext);
            if (raidEvent == null)
            {
                return;
            }

            if (raid.DelayGeneration)
            {
                sink.DelaySolo(raidEvent, DiaryEvent.InitiatorRole, raid.GenerationReadyTick);
            }
            else
            {
                sink.QueueSolo(raidEvent, DiaryEvent.InitiatorRole);
            }
        }
    }
}

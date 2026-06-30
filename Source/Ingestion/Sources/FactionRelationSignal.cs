// Faction-relation ingestion signal — the impure capture+emit half of player diplomacy transitions.
// Faction.SetRelationDirect can be called for either side of a relation, so the patch normalizes the
// non-player faction and this fan-out writes one capped solo entry per eligible colonist.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Colony-wide fan-out for a player relation-kind transition with another faction.
    /// </summary>
    public sealed class FactionRelationFanoutSignal : DiaryFanoutSignal
    {
        private readonly bool valid;
        private readonly string colonyDedupKey;
        private readonly Faction faction;
        private readonly string defName;
        private readonly string label;
        private readonly string oldKind;
        private readonly string newKind;
        private readonly int oldGoodwill;
        private readonly int newGoodwill;
        private readonly string reason;
        private readonly DiaryInteractionGroupDef group;

        public FactionRelationFanoutSignal(
            Faction faction,
            string oldKind,
            string newKind,
            int oldGoodwill,
            int newGoodwill,
            string reason)
        {
            if (!DiaryGameComponent.GamePlaying || faction == null || faction.def == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            string normalizedOld = FactionRelationEventData.NormalizeRelationKind(oldKind);
            string normalizedNew = FactionRelationEventData.NormalizeRelationKind(newKind);
            if (string.IsNullOrWhiteSpace(normalizedNew)
                || string.Equals(normalizedOld, normalizedNew, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DiaryInteractionGroupDef classified = InteractionGroups.ClassifyFactionRelation(normalizedNew);
            if (classified == null || !PawnDiaryMod.Settings.IsGroupEnabled(classified.defName))
            {
                return;
            }

            this.faction = faction;
            this.oldKind = normalizedOld;
            this.newKind = normalizedNew;
            this.oldGoodwill = oldGoodwill;
            this.newGoodwill = newGoodwill;
            this.reason = DiaryLineCleaner.CleanLine(reason);
            group = classified;
            defName = faction.def.defName;
            label = FactionLabel(faction);
            colonyDedupKey = "faction_relation|" + defName + "|" + normalizedOld + "|" + normalizedNew;
            valid = true;
        }

        public override string ColonyDedupKey => valid ? colonyDedupKey : string.Empty;

        public override int ColonyDedupTicks => DiarySignalPolicies.FactionRelationDedupTicks;

        public override IEnumerable<DiarySignal> PerPawnSignals()
        {
            if (!valid)
            {
                yield break;
            }

            int cap = DiarySignalPolicies.FactionRelationMaxFanoutPawns;
            int emitted = 0;
            HashSet<string> seen = new HashSet<string>();
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                List<Pawn> colonists = map?.mapPawns?.FreeColonists;
                if (colonists == null)
                {
                    continue;
                }

                for (int j = 0; j < colonists.Count; j++)
                {
                    Pawn pawn = colonists[j];
                    if (pawn == null || !DiaryGameComponent.IsDiaryEligible(pawn))
                    {
                        continue;
                    }

                    string pawnId = pawn.GetUniqueLoadID();
                    if (!seen.Add(pawnId))
                    {
                        continue;
                    }

                    yield return new FactionRelationPawnSignal(this, pawn, pawnId);
                    emitted++;
                    if (cap > 0 && emitted >= cap)
                    {
                        yield break;
                    }
                }
            }
        }

        private static string FactionLabel(Faction faction)
        {
            string label = DiaryLineCleaner.CleanLine(faction == null ? null : faction.Name);
            return string.IsNullOrWhiteSpace(label) ? faction?.def?.defName ?? "faction" : label;
        }

        private sealed class FactionRelationPawnSignal : DiarySignal
        {
            private readonly FactionRelationFanoutSignal source;
            private readonly Pawn pawn;
            private readonly FactionRelationEventData payload;

            public FactionRelationPawnSignal(FactionRelationFanoutSignal source, Pawn pawn, string pawnId)
            {
                this.source = source;
                this.pawn = pawn;
                payload = new FactionRelationEventData
                {
                    PawnId = pawnId,
                    Tick = Find.TickManager.TicksGame,
                    DefName = source.defName,
                    Label = source.label,
                    OldRelationKind = source.oldKind,
                    NewRelationKind = source.newKind,
                    OldGoodwill = source.oldGoodwill,
                    NewGoodwill = source.newGoodwill,
                    Reason = source.reason
                };
            }

            public override DiaryEventData Payload => payload;

            public override CaptureContext BuildContext()
            {
                return DiaryGameComponent.BuildCaptureContext(
                    eligible: true,
                    userEnabled: true,
                    signalEnabled: DiarySignalPolicies.Enabled(DiarySignalPolicies.FactionRelation),
                    ambientSignalEnabled: true);
            }

            public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
            {
                if (decision != CaptureDecision.GenerateSolo)
                {
                    return;
                }

                string context = FactionRelationEventData.BuildGameContext(
                    source.defName, source.label, source.oldKind, source.newKind,
                    source.oldGoodwill, source.newGoodwill, source.reason);
                string text = "PawnDiary.Event.FactionRelationChanged".Translate(
                    pawn.LabelShortCap, source.label, RelationLabel(source.oldKind),
                    RelationLabel(source.newKind)).Resolve();
                string instruction = InteractionGroups.InstructionForGroup(source.group);

                DiaryEvent diaryEvent = sink.AddSoloEvent(
                    pawn, null, source.defName, source.label, text, instruction, context);
                if (diaryEvent == null)
                {
                    return;
                }

                sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
            }

            private static string RelationLabel(string relationKind)
            {
                string normalized = FactionRelationEventData.NormalizeRelationKind(relationKind);
                if (string.Equals(normalized, FactionRelationEventData.Ally, StringComparison.OrdinalIgnoreCase))
                {
                    return "PawnDiary.Event.FactionRelation.Kind.Ally".Translate().Resolve();
                }

                if (string.Equals(normalized, FactionRelationEventData.Hostile, StringComparison.OrdinalIgnoreCase))
                {
                    return "PawnDiary.Event.FactionRelation.Kind.Hostile".Translate().Resolve();
                }

                return "PawnDiary.Event.FactionRelation.Kind.Neutral".Translate().Resolve();
            }
        }
    }
}

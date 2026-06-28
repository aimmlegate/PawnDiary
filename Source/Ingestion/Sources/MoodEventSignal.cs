// Mood-event ingestion signal — the impure capture+emit half of the "mood-affecting game condition"
// source (GameConditionManager.RegisterCondition). Replaces the old DiaryGameComponent.RecordMoodEvent.
// A colony-wide FAN-OUT: one solo entry per eligible colonist on every affected map, with a single
// condition-level dedup window. The mood-impact direction (positive/negative/neutral) is computed
// PER PAWN because some conditions affect different sexes differently (e.g. PsychicSuppressorMale),
// so each child computes its own impact in its constructor.
//
// Pure decision + game-context live in Source/Capture/Events/MoodEventData.cs.
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Colony-wide mood-event fan-out. Built by <see cref="GameConditionStartPatch"/> and submitted via
    /// <see cref="DiaryEvents.Submit(DiaryFanoutSignal)"/>. The condition-level gates (user toggle) run
    /// in the constructor; if they fail the fan-out yields nothing.
    /// </summary>
    public sealed class MoodEventFanoutSignal : DiaryFanoutSignal
    {
        private readonly bool valid;
        private readonly string colonyDedupKey;

        internal GameCondition Condition { get; }
        internal string ConditionDefName { get; }
        internal string ConditionLabel { get; }
        internal string Instruction { get; }
        // The condition's own thought offset is identical for every colonist, so compute it once (it
        // scans the whole ThoughtDef database) instead of inside the per-pawn loop.
        internal float ConditionThoughtOffset { get; }

        public MoodEventFanoutSignal(GameCondition condition)
        {
            if (!DiaryGameComponent.GamePlaying || condition == null || condition.def == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!PawnDiaryMod.Settings.IsMoodEventEnabled(condition.def))
            {
                return;
            }

            Condition = condition;
            GameConditionDef conditionDef = condition.def;
            ConditionDefName = conditionDef.defName;
            ConditionLabel = DiaryLineCleaner.CleanLine(conditionDef.LabelCap.Resolve());
            Instruction = InteractionGroups.InstructionForMoodEvent(conditionDef);
            ConditionThoughtOffset = MoodImpactClassifier.GetMoodOffsetFromConditionThoughts(conditionDef);
            colonyDedupKey = "moodevent|" + ConditionDefName + "|" + condition.uniqueID;
            valid = true;
        }

        public override string ColonyDedupKey => valid ? colonyDedupKey : string.Empty;

        public override int ColonyDedupTicks => DiaryTuning.Current.moodEventDedupTicks;

        public override IEnumerable<DiarySignal> PerPawnSignals()
        {
            if (!valid)
            {
                yield break;
            }

            // AffectedMaps can be empty for planet-wide conditions, in which case all maps in play
            // are affected.
            List<Map> affectedMaps = Condition.AffectedMaps;
            if (affectedMaps == null || affectedMaps.Count == 0)
            {
                affectedMaps = Find.Maps;
            }

            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < affectedMaps.Count; i++)
            {
                Map map = affectedMaps[i];
                if (map == null || map.mapPawns == null)
                {
                    continue;
                }

                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int j = 0; j < colonists.Count; j++)
                {
                    Pawn pawn = colonists[j];
                    if (pawn == null || !DiaryGameComponent.IsDiaryEligible(pawn))
                    {
                        continue;
                    }

                    // A pawn could be on multiple maps during transitions; skip duplicates.
                    string pawnId = pawn.GetUniqueLoadID();
                    if (!seen.Add(pawnId))
                    {
                        continue;
                    }

                    yield return new MoodEventPawnSignal(this, pawn, pawnId);
                }
            }
        }
    }

    /// <summary>
    /// One colonist's solo mood-event entry, with this pawn's own mood-impact direction.
    /// </summary>
    internal sealed class MoodEventPawnSignal : DiarySignal
    {
        private readonly MoodEventFanoutSignal source;
        private readonly Pawn pawn;
        private readonly string moodImpact;
        private readonly MoodEventData payload;

        public MoodEventPawnSignal(MoodEventFanoutSignal source, Pawn pawn, string pawnId)
        {
            this.source = source;
            this.pawn = pawn;
            moodImpact = MoodImpactClassifier.DetermineMoodImpact(source.Condition, pawn, source.ConditionThoughtOffset);
            payload = new MoodEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = source.ConditionDefName,
                Label = source.ConditionLabel,
                MoodImpact = moodImpact,
            };
        }

        public override DiaryEventData Payload => payload;

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

            string perPawnContext = MoodEventData.BuildGameContext(
                source.ConditionDefName, source.ConditionLabel, moodImpact);

            string text = MoodImpact.PickText(moodImpact,
                "PawnDiary.Event.MoodEventPositive", "PawnDiary.Event.MoodEventNegative", "PawnDiary.Event.MoodEvent",
                pawn.LabelShortCap, source.ConditionLabel);

            DiaryEvent moodEvent = sink.AddSoloEvent(pawn, null, source.ConditionDefName, source.ConditionLabel,
                text, source.Instruction, perPawnContext);
            if (moodEvent == null)
            {
                return;
            }

            moodEvent.moodImpact = moodImpact;
            sink.QueueSolo(moodEvent, DiaryEvent.InitiatorRole);
        }
    }
}

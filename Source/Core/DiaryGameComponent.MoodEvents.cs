// Mood events — the GameConditionManager.RegisterCondition hook's diary flow. A mood-affecting
// game condition (aurora, eclipse, psychic drone, toxic fallout, …) is recorded as a separate solo
// DiaryEvent for each eligible colonist on every affected map, because each pawn experiences it
// independently — and the mood impact direction (positive/negative/neutral) is computed per pawn,
// since some conditions affect different sexes differently (e.g. PsychicSuppressorMale). The event
// carries a "mood_event=" game-context marker so the UI classifies it into the MoodEvent domain.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Records a mood-affecting GameCondition (aurora, eclipse, psychic drone, toxic fallout,
        /// etc.) as solo diary events for each eligible colonist on affected maps. Called by the
        /// <see cref="GameConditionStartPatch"/> Harmony postfix on
        /// <c>GameConditionManager.RegisterCondition</c>.
        /// </summary>
        public void RecordMoodEvent(GameCondition condition)
        {
            if (!CanRecordGameplayEventNow() || condition == null || condition.def == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!PawnDiaryMod.Settings.IsMoodEventEnabled(condition.def))
            {
                return;
            }

            GameConditionDef conditionDef = condition.def;
            string conditionDefName = conditionDef.defName;
            string conditionLabel = DiaryContextBuilder.CleanLine(conditionDef.LabelCap.Resolve());

            // Dedup the concrete GameCondition instance. Only mark the key after at least one pawn
            // records an entry, so an empty transition map cannot consume the whole window.
            string dedupKey = "moodevent|" + conditionDefName + "|" + condition.uniqueID;
            if (IsRecentlyRecorded(recentMoodEvents, dedupKey, DiaryTuning.Current.moodEventDedupTicks))
            {
                return;
            }

            string instruction = PawnDiaryMod.Settings.InstructionForMoodEvent(conditionDef);

            // The condition's own thought offset is identical for every colonist, so compute it once
            // here (it scans the whole ThoughtDef database) instead of inside the per-pawn loop.
            float conditionThoughtOffset = DiaryContextBuilder.GetMoodOffsetFromConditionThoughts(conditionDef);

            // Base gameContext shared by all pawns experiencing this condition.
            string baseContext = "mood_event=" + conditionDefName
                + "; label=" + conditionLabel;

            // Collect all eligible colonists on maps affected by this condition.
            // AffectedMaps can be empty for planet-wide conditions, in which case we
            // check all maps in play.
            List<Map> affectedMaps = condition.AffectedMaps;
            if (affectedMaps == null || affectedMaps.Count == 0)
            {
                affectedMaps = Find.Maps;
            }

            HashSet<string> recordedPawnIds = new HashSet<string>();
            bool recordedAny = false;
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
                    if (pawn == null || !IsDiaryEligible(pawn))
                    {
                        continue;
                    }

                    // A pawn could be on multiple maps during transitions; skip duplicates.
                    string pawnId = pawn.GetUniqueLoadID();
                    if (recordedPawnIds.Contains(pawnId))
                    {
                        continue;
                    }

                    recordedPawnIds.Add(pawnId);

                    // Each affected colonist gets their own solo diary event, since each
                    // pawn experiences the mood event independently. The mood impact direction
                    // (positive/negative/neutral) is determined per-pawn because some conditions
                    // (e.g. PsychicSuppressorMale) affect different sexes differently.
                    string moodImpact = DiaryContextBuilder.DetermineMoodImpact(condition, pawn, conditionThoughtOffset);
                    string perPawnContext = baseContext + "; mood_impact=" + moodImpact;

                    string text = MoodImpact.PickText(moodImpact,
                        "PawnDiary.Event.MoodEventPositive", "PawnDiary.Event.MoodEventNegative", "PawnDiary.Event.MoodEvent",
                        pawn.LabelShortCap, conditionLabel);

                    DiaryEvent moodEvent = AddSoloEvent(pawn, null, conditionDefName, conditionLabel,
                        text, instruction, perPawnContext);
                    if (moodEvent == null)
                    {
                        continue;
                    }

                    recordedAny = true;
                    moodEvent.moodImpact = moodImpact;
                    QueueLlmRewrite(moodEvent, DiaryEvent.InitiatorRole);
                }
            }

            if (recordedAny)
            {
                MarkRecentlyRecorded(recentMoodEvents, dedupKey, DiaryTuning.Current.moodEventDedupTicks);
            }
        }
    }
}

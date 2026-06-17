// Ambient thought notes. Temporary memories can fire often for small mood texture (kind words,
// nuzzling, parties, bad barracks, etc.). This file lets XML tuning mark those thoughts as
// day-note material: they accumulate per pawn/day and write one solo diary memory instead of one
// entry per thought. Major thoughts still flow through DiaryGameComponent.Thoughts.cs immediately.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Adds one temporary thought to the pawn's ambient thought note for the current day.
        /// </summary>
        private void RecordAmbientThought(Pawn pawn, ThoughtDef thoughtDef, string label, float moodOffset,
            string moodImpact, string instruction)
        {
            if (pawn == null || thoughtDef == null)
            {
                return;
            }

            string key = AmbientThoughtKey(pawn, CurrentDayIndex);
            if (writtenAmbientThoughtNotes.Contains(key))
            {
                return;
            }

            PendingAmbientThoughtNote note;
            if (!pendingAmbientThoughtNotes.TryGetValue(key, out note))
            {
                int now = Find.TickManager.TicksGame;
                note = new PendingAmbientThoughtNote
                {
                    key = key,
                    pawn = pawn,
                    pawnId = pawn.GetUniqueLoadID(),
                    dayIndex = CurrentDayIndex,
                    firstTick = now,
                    lastTick = now,
                    instruction = instruction
                };
                pendingAmbientThoughtNotes[key] = note;
            }

            note.eventCount++;
            note.lastTick = Find.TickManager.TicksGame;
            note.totalMoodOffset += moodOffset;
            CountMoodImpact(note, moodImpact);

            if (note.sampleLines.Count < AmbientThoughtMaxSampleLines)
            {
                note.sampleLines.Add(AmbientThoughtLine(label, moodOffset));
            }
        }

        /// <summary>
        /// Flushes ambient thought notes that reached the next day or have been quiet long enough.
        /// </summary>
        private void FlushReadyAmbientThoughtNotes()
        {
            if (pendingAmbientThoughtNotes.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            int currentDay = CurrentDayIndex;
            List<string> keysToFlush = new List<string>();
            foreach (KeyValuePair<string, PendingAmbientThoughtNote> pair in pendingAmbientThoughtNotes)
            {
                PendingAmbientThoughtNote note = pair.Value;
                if (note == null
                    || note.dayIndex != currentDay
                    || now - note.lastTick >= AmbientThoughtWindowTicks)
                {
                    keysToFlush.Add(pair.Key);
                }
            }

            FlushAmbientThoughtNotes(keysToFlush);
        }

        /// <summary>
        /// Flushes every pending ambient thought note immediately, used before saving.
        /// </summary>
        private void FlushAllAmbientThoughtNotes()
        {
            if (pendingAmbientThoughtNotes.Count == 0)
            {
                return;
            }

            FlushAmbientThoughtNotes(new List<string>(pendingAmbientThoughtNotes.Keys));
        }

        /// <summary>
        /// Flushes each ambient thought note identified by key.
        /// </summary>
        private void FlushAmbientThoughtNotes(List<string> keysToFlush)
        {
            if (keysToFlush == null)
            {
                return;
            }

            for (int i = 0; i < keysToFlush.Count; i++)
            {
                PendingAmbientThoughtNote note;
                if (pendingAmbientThoughtNotes.TryGetValue(keysToFlush[i], out note))
                {
                    FlushAmbientThoughtNote(keysToFlush[i], note);
                }
            }
        }

        /// <summary>
        /// Flushes only this pawn's ambient thought note when it already has enough material. Sleep
        /// should feel like the pawn writing a diary entry, not like a reason to publish one tiny mood.
        /// </summary>
        private void FlushAmbientThoughtNotesForPawn(Pawn pawn)
        {
            if (pawn == null || pendingAmbientThoughtNotes.Count == 0)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            List<string> keysToFlush = new List<string>();
            foreach (KeyValuePair<string, PendingAmbientThoughtNote> pair in pendingAmbientThoughtNotes)
            {
                PendingAmbientThoughtNote note = pair.Value;
                if (note != null
                    && string.Equals(note.pawnId, pawnId, StringComparison.Ordinal)
                    && note.eventCount >= AmbientThoughtMinEventsToWrite)
                {
                    keysToFlush.Add(pair.Key);
                }
            }

            FlushAmbientThoughtNotes(keysToFlush);
        }

        /// <summary>
        /// Converts an ambient thought batch into one solo diary memory, or drops it if too thin.
        /// </summary>
        private void FlushAmbientThoughtNote(string key, PendingAmbientThoughtNote note)
        {
            pendingAmbientThoughtNotes.Remove(key);

            if (note == null || note.pawn == null || !IsDiaryEligible(note.pawn))
            {
                return;
            }

            if (note.eventCount < AmbientThoughtMinEventsToWrite)
            {
                return;
            }

            string label = "PawnDiary.Event.AmbientThoughtLabel".Translate().Resolve();
            string moodImpact = AmbientThoughtMoodImpact(note);
            string gameContext = "thought=ThoughtAmbientDay"
                + "; batch=ambient_day_note"
                + "; events=" + note.eventCount
                + "; day=" + note.dayIndex
                + "; mood_impact=" + moodImpact
                + "; mood_offset_sum=" + note.totalMoodOffset.ToString("F1", CultureInfo.InvariantCulture)
                + "; first_tick=" + note.firstTick
                + "; last_tick=" + note.lastTick;

            string text = BuildAmbientThoughtText(note);
            string instruction = AmbientThoughtInstruction(note.instruction);
            DiaryEvent diaryEvent = AddSoloEvent(note.pawn, null, "ThoughtAmbientDay", label,
                text, instruction, gameContext);
            diaryEvent.moodImpact = moodImpact;
            writtenAmbientThoughtNotes.Add(key);
            QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Builds the raw evidence text for the LLM. The instruction prevents list-like output.
        /// </summary>
        private static string BuildAmbientThoughtText(PendingAmbientThoughtNote note)
        {
            if (note.sampleLines.Count == 0)
            {
                return "PawnDiary.Event.AmbientThoughtFallback".Translate(note.pawn.LabelShortCap).Resolve();
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("PawnDiary.Event.AmbientThoughtHeader".Translate().Resolve());
            for (int i = 0; i < note.sampleLines.Count; i++)
            {
                builder.Append("\n").Append(i + 1).Append(". ").Append(note.sampleLines[i]);
            }

            if (note.eventCount > note.sampleLines.Count)
            {
                builder.Append("\n").Append("... ")
                    .Append("PawnDiary.Event.AmbientDayMore".Translate(note.eventCount - note.sampleLines.Count).Resolve());
            }

            return builder.ToString();
        }

        /// <summary>
        /// Combines the base thought instruction with ambient-note guidance.
        /// </summary>
        private static string AmbientThoughtInstruction(string baseInstruction)
        {
            string instruction = DiaryContextBuilder.CleanLine(baseInstruction);
            string ambientInstruction = "PawnDiary.Event.AmbientThoughtInstruction".Translate().Resolve();
            if (string.IsNullOrWhiteSpace(instruction))
            {
                return ambientInstruction;
            }

            return instruction + "; " + ambientInstruction;
        }

        /// <summary>
        /// Adds a mood impact count for later positive/negative/neutral classification.
        /// </summary>
        private static void CountMoodImpact(PendingAmbientThoughtNote note, string moodImpact)
        {
            if (string.Equals(moodImpact, MoodImpact.Positive, StringComparison.OrdinalIgnoreCase))
            {
                note.positiveCount++;
                return;
            }

            if (string.Equals(moodImpact, MoodImpact.Negative, StringComparison.OrdinalIgnoreCase))
            {
                note.negativeCount++;
                return;
            }

            note.neutralCount++;
        }

        /// <summary>
        /// Picks the overall impact direction for the ambient note.
        /// </summary>
        private static string AmbientThoughtMoodImpact(PendingAmbientThoughtNote note)
        {
            if (note.positiveCount > note.negativeCount)
            {
                return MoodImpact.Positive;
            }

            if (note.negativeCount > note.positiveCount)
            {
                return MoodImpact.Negative;
            }

            return MoodImpact.Neutral;
        }

        /// <summary>
        /// Formats one thought as compact prompt evidence.
        /// </summary>
        private static string AmbientThoughtLine(string label, float moodOffset)
        {
            return DiaryContextBuilder.CleanLine(label) + " (" + moodOffset.ToString("F1", CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// Key for one pawn's ambient thought note on one in-game day.
        /// </summary>
        private static string AmbientThoughtKey(Pawn pawn, int dayIndex)
        {
            return "thoughtAmbient|" + pawn.GetUniqueLoadID() + "|" + dayIndex;
        }

        private static int AmbientThoughtWindowTicks
        {
            get
            {
                return Math.Max(0, DiaryTuning.Current.thoughtAmbientWindowTicks);
            }
        }

        private static int AmbientThoughtMinEventsToWrite
        {
            get
            {
                return Math.Max(1, DiaryTuning.Current.thoughtAmbientMinEventsToWrite);
            }
        }

        private static int AmbientThoughtMaxSampleLines
        {
            get
            {
                return Math.Max(1, DiaryTuning.Current.thoughtAmbientMaxSampleLines);
            }
        }

        /// <summary>
        /// Accumulates low-impact temporary memories for one pawn/day.
        /// </summary>
        private class PendingAmbientThoughtNote
        {
            public string key;
            public Pawn pawn;
            public string pawnId;
            public int dayIndex;
            public int firstTick;
            public int lastTick;
            public string instruction;
            public int eventCount;
            public float totalMoodOffset;
            public int positiveCount;
            public int negativeCount;
            public int neutralCount;
            public readonly List<string> sampleLines = new List<string>();
        }
    }
}

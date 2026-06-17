// The end-of-day "reflection". The old ambient day note was built only from low-stakes filler
// (small talk, passing thoughts), so it read thin. This file builds a richer memory instead: when a
// colonist beds down, it gathers the day's candidate signals from several collectors — the day's
// major diary events, big opinion swings toward other colonists, newly-appeared afflictions, plus
// the filler as light background — then runs a weighted-random selection so only the MOST important
// few survive, and writes one solo reflective entry. It deliberately coexists with the per-event
// entries that already fired in the moment: this is the pawn looking back on the whole day.
//
// All state here is transient (cleared on load, re-derived live), like the existing pending notes,
// so this touches no save schema. The opinion baseline is re-snapshotted on load and at each day
// rollover (see DiaryGameComponent.cs); a pawn who sleeps past midnight therefore gets a weaker
// opinion-delta signal that day — an accepted v1 limitation, the other signals still carry it.
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
        // Major afflictions seen today, keyed by "pawnId|dayIndex". Accumulated by the AddHediff hook
        // and consumed when that pawn's reflection is written.
        private readonly Dictionary<string, List<DayHediffRecord>> pendingDayHediffs =
            new Dictionary<string, List<DayHediffRecord>>();

        // Each free colonist's opinion of every other, snapshotted at the start of the current day,
        // keyed "fromId|toId". Diffed at reflection time to detect a social shift. Re-snapshotted on
        // load and at day rollover (DiaryGameComponent.cs), so it is never persisted.
        private readonly Dictionary<string, int> dayStartOpinions = new Dictionary<string, int>();

        // Day index the dayStartOpinions snapshot was taken for; -1 means "not snapshotted yet".
        private int opinionSnapshotDay = -1;

        // "pawnId|dayIndex" reflections already written, so a pawn waking and re-sleeping in one day
        // does not get a second reflection. Transient, matching the existing written-note sets.
        private readonly HashSet<string> writtenDayReflections = new HashSet<string>();

        /// <summary>
        /// Sleep-path entry point: writes this pawn's reflection for the current day (once), weaving
        /// together a weighted-random selection of the day's most notable signals. Quiet days with
        /// nothing worth noting produce no entry. Used in place of the per-source ambient note flush
        /// when the day-summary feature is enabled.
        /// </summary>
        private void FlushDaySummaryForPawn(Pawn pawn)
        {
            if (pawn == null || !IsDiaryEligible(pawn))
            {
                return;
            }

            int day = CurrentDayIndex;
            string pawnId = pawn.GetUniqueLoadID();
            string dayKey = DaySummaryKey(pawnId, day);
            if (writtenDayReflections.Contains(dayKey))
            {
                return;
            }

            // Gather candidates from every source, then keep only the most important few.
            List<DaySummarySignal> candidates = new List<DaySummarySignal>();
            CollectEventSignals(pawnId, day, candidates);
            CollectOpinionSignals(pawn, candidates);
            CollectHediffSignals(dayKey, candidates);
            int fillerCount = CollectFillerSignal(pawnId, day, candidates);

            // Always release this pawn/day's filler + hediffs so they never separately emit, even
            // when nothing is selected. (Consuming filler also keeps the old fallback path from
            // re-emitting it on day change or save.)
            ConsumePawnDayFiller(pawnId, day);
            pendingDayHediffs.Remove(dayKey);

            if (candidates.Count == 0)
            {
                return;
            }

            writtenDayReflections.Add(dayKey);

            List<DaySummarySignal> highlights = SelectHighlights(candidates, DaySummaryMaxHighlights);
            highlights.Sort((a, b) => b.weight.CompareTo(a.weight));

            string label = "PawnDiary.Event.DayReflectionLabel".Translate().Resolve();
            string text = BuildDayReflectionText(pawn, highlights);
            string instruction = "PawnDiary.Event.DayReflectionInstruction".Translate(pawn.LabelShortCap).Resolve();

            StringBuilder tags = new StringBuilder();
            for (int i = 0; i < highlights.Count; i++)
            {
                if (tags.Length > 0)
                {
                    tags.Append(", ");
                }

                tags.Append(highlights[i].contextTag);
            }

            string gameContext = "day_reflection=true"
                + "; day=" + day
                + "; highlights=" + highlights.Count
                + "; candidates=" + candidates.Count
                + "; filler_moments=" + fillerCount
                + "; signals=" + tags;

            DiaryEvent diaryEvent = AddSoloEvent(pawn, null, "DayReflection", label, text, instruction, gameContext);
            QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Adds one signal per important diary event the pawn took part in today. Filler/ambient
        /// entries (not "important") and the reflection's own def are skipped.
        /// </summary>
        private void CollectEventSignals(string pawnId, int day, List<DaySummarySignal> candidates)
        {
            DiaryTuningDef tuning = DiaryTuning.Current;

            // diaryEvents is appended in tick order, so scan newest-first and stop once we drop below
            // the target day — this bounds the work to today's events instead of the whole history.
            for (int i = diaryEvents.Count - 1; i >= 0; i--)
            {
                DiaryEvent ev = diaryEvents[i];
                if (ev == null)
                {
                    continue;
                }

                int eventDay = DayIndexForGameTick(ev.tick);
                if (eventDay > day)
                {
                    continue;
                }

                if (eventDay < day)
                {
                    break;
                }

                if (string.Equals(ev.interactionDefName, "DayReflection", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string role = ev.RoleForPawn(pawnId);
                if (role == null || !ev.IsImportant())
                {
                    continue;
                }

                string line = EventEvidenceLine(ev, role);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                float weight = ev.IsCombatRelated() ? tuning.daySummaryWeightCriticalEvent : tuning.daySummaryWeightMajorEvent;
                candidates.Add(new DaySummarySignal(weight, line, "event:" + ev.interactionDefName));
            }
        }

        /// <summary>
        /// Adds one signal per other colonist this pawn's opinion of swung noticeably today, versus
        /// the day-start snapshot. Weight scales with the size of the swing.
        /// </summary>
        private void CollectOpinionSignals(Pawn pawn, List<DaySummarySignal> candidates)
        {
            if (pawn.relations == null || dayStartOpinions.Count == 0)
            {
                return;
            }

            DiaryTuningDef tuning = DiaryTuning.Current;
            int threshold = Math.Max(1, tuning.daySummaryOpinionDeltaThreshold);
            string pawnId = pawn.GetUniqueLoadID();

            List<Pawn> colonists = SnapshotFreeColonists();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn other = colonists[i];
                if (other == null || other == pawn)
                {
                    continue;
                }

                int baseline;
                if (!dayStartOpinions.TryGetValue(pawnId + "|" + other.GetUniqueLoadID(), out baseline))
                {
                    continue;
                }

                int delta = pawn.relations.OpinionOf(other) - baseline;
                if (Mathf.Abs(delta) < threshold)
                {
                    continue;
                }

                // base weight, scaled up (capped at 2x) the further past the threshold the swing went.
                float weight = tuning.daySummaryWeightOpinionShift * Mathf.Min(2f, (float)Mathf.Abs(delta) / threshold);
                string line = (delta > 0
                    ? "PawnDiary.Event.DayReflectionOpinionWarmed".Translate(other.LabelShortCap)
                    : "PawnDiary.Event.DayReflectionOpinionCooled".Translate(other.LabelShortCap)).Resolve();
                candidates.Add(new DaySummarySignal(weight, line, "opinion:" + (delta > 0 ? "+" : "") + delta));
            }
        }

        /// <summary>
        /// Adds one signal per major affliction that appeared for this pawn today.
        /// </summary>
        private void CollectHediffSignals(string dayKey, List<DaySummarySignal> candidates)
        {
            List<DayHediffRecord> list;
            if (!pendingDayHediffs.TryGetValue(dayKey, out list) || list == null)
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                string key = list[i].progressed
                    ? "PawnDiary.Event.DayReflectionHediffProgressed"
                    : "PawnDiary.Event.DayReflectionHediff";
                string line = key.Translate(list[i].label).Resolve();
                float weight = list[i].weight > 0f ? list[i].weight : DiaryTuning.Current.daySummaryWeightHediff;
                candidates.Add(new DaySummarySignal(weight, line, "hediff:" + list[i].defName));
            }
        }

        /// <summary>
        /// Adds a single low-weight background signal when the day held enough small talk / passing
        /// feelings to be worth a mention. Returns the total filler-moment count for context.
        /// </summary>
        private int CollectFillerSignal(string pawnId, int day, List<DaySummarySignal> candidates)
        {
            int fillerCount = CountPawnDayFiller(pawnId, day);
            if (fillerCount >= 2)
            {
                string line = "PawnDiary.Event.DayReflectionFillerLine".Translate().Resolve();
                candidates.Add(new DaySummarySignal(DiaryTuning.Current.daySummaryWeightFiller, line, "filler:" + fillerCount));
            }

            return fillerCount;
        }

        /// <summary>
        /// Weighted-random selection without replacement: draws up to <paramref name="max"/> signals,
        /// each draw favoring higher-weight candidates. High-weight signals (a raid, a new disease)
        /// almost always survive; medium ones rotate for variety day to day.
        /// </summary>
        private static List<DaySummarySignal> SelectHighlights(List<DaySummarySignal> candidates, int max)
        {
            List<DaySummarySignal> pool = new List<DaySummarySignal>(candidates);
            List<DaySummarySignal> chosen = new List<DaySummarySignal>();

            while (chosen.Count < max && pool.Count > 0)
            {
                float total = 0f;
                for (int i = 0; i < pool.Count; i++)
                {
                    total += Mathf.Max(0.0001f, pool[i].weight);
                }

                float roll = Rand.Value * total;
                float acc = 0f;
                int picked = pool.Count - 1;
                for (int i = 0; i < pool.Count; i++)
                {
                    acc += Mathf.Max(0.0001f, pool[i].weight);
                    if (roll <= acc)
                    {
                        picked = i;
                        break;
                    }
                }

                chosen.Add(pool[picked]);
                pool.RemoveAt(picked);
            }

            return chosen;
        }

        /// <summary>
        /// Formats the selected highlights into the raw evidence text. The instruction tells the LLM
        /// to reflect rather than list, so this is a small set of cues, not the final prose.
        /// </summary>
        private static string BuildDayReflectionText(Pawn pawn, List<DaySummarySignal> highlights)
        {
            if (highlights == null || highlights.Count == 0)
            {
                return "PawnDiary.Event.DayReflectionFallback".Translate(pawn.LabelShortCap).Resolve();
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("PawnDiary.Event.DayReflectionHeader".Translate(pawn.LabelShortCap).Resolve());
            for (int i = 0; i < highlights.Count; i++)
            {
                builder.Append("\n").Append("- ").Append(highlights[i].evidenceLine);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Builds a compact evidence cue for a day event from the pawn's point of view.
        /// </summary>
        private static string EventEvidenceLine(DiaryEvent ev, string role)
        {
            string label = DiaryContextBuilder.CleanLine(ev.interactionLabel);
            string body = DiaryContextBuilder.CleanLine(ev.DisplayTextForRole(role));
            body = TruncateForEvidence(body);

            if (string.IsNullOrWhiteSpace(body))
            {
                return label;
            }

            return string.IsNullOrWhiteSpace(label) ? body : label + " — " + body;
        }

        /// <summary>
        /// Total filler moments (ambient interaction notes + passing-thought note) recorded for this
        /// pawn on this day across all ambient groups.
        /// </summary>
        private int CountPawnDayFiller(string pawnId, int day)
        {
            int count = 0;
            foreach (KeyValuePair<string, PendingAmbientInteractionNote> pair in pendingAmbientInteractionNotes)
            {
                PendingAmbientInteractionNote note = pair.Value;
                if (note != null && note.dayIndex == day && string.Equals(note.pawnId, pawnId, StringComparison.Ordinal))
                {
                    count += note.eventCount;
                }
            }

            foreach (KeyValuePair<string, PendingAmbientThoughtNote> pair in pendingAmbientThoughtNotes)
            {
                PendingAmbientThoughtNote note = pair.Value;
                if (note != null && note.dayIndex == day && string.Equals(note.pawnId, pawnId, StringComparison.Ordinal))
                {
                    count += note.eventCount;
                }
            }

            return count;
        }

        /// <summary>
        /// Removes this pawn/day's pending ambient notes so they fold into the reflection instead of
        /// emitting their own entries, and marks their keys written so they are not recreated today.
        /// </summary>
        private void ConsumePawnDayFiller(string pawnId, int day)
        {
            List<string> interactionKeys = new List<string>();
            foreach (KeyValuePair<string, PendingAmbientInteractionNote> pair in pendingAmbientInteractionNotes)
            {
                PendingAmbientInteractionNote note = pair.Value;
                if (note != null && note.dayIndex == day && string.Equals(note.pawnId, pawnId, StringComparison.Ordinal))
                {
                    interactionKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < interactionKeys.Count; i++)
            {
                pendingAmbientInteractionNotes.Remove(interactionKeys[i]);
                writtenAmbientInteractionNotes.Add(interactionKeys[i]);
            }

            List<string> thoughtKeys = new List<string>();
            foreach (KeyValuePair<string, PendingAmbientThoughtNote> pair in pendingAmbientThoughtNotes)
            {
                PendingAmbientThoughtNote note = pair.Value;
                if (note != null && note.dayIndex == day && string.Equals(note.pawnId, pawnId, StringComparison.Ordinal))
                {
                    thoughtKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < thoughtKeys.Count; i++)
            {
                pendingAmbientThoughtNotes.Remove(thoughtKeys[i]);
                writtenAmbientThoughtNotes.Add(thoughtKeys[i]);
            }
        }

        /// <summary>
        /// Re-snapshots every free colonist's opinion of every other for the current day, and prunes
        /// stale pending hediffs from earlier days. Called on load and at each day rollover.
        /// </summary>
        private void SnapshotDayStartOpinions()
        {
            dayStartOpinions.Clear();
            List<Pawn> colonists = SnapshotFreeColonists();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn from = colonists[i];
                if (from == null || from.relations == null)
                {
                    continue;
                }

                string fromId = from.GetUniqueLoadID();
                for (int j = 0; j < colonists.Count; j++)
                {
                    Pawn to = colonists[j];
                    if (to == null || to == from)
                    {
                        continue;
                    }

                    dayStartOpinions[fromId + "|" + to.GetUniqueLoadID()] = from.relations.OpinionOf(to);
                }
            }

            opinionSnapshotDay = CurrentDayIndex;
            PruneStaleDayHediffs(CurrentDayIndex);
        }

        /// <summary>
        /// Drops accumulated hediffs from days before the current one (a pawn who never bedded down
        /// to trigger a reflection), so the map cannot grow without bound.
        /// </summary>
        private void PruneStaleDayHediffs(int currentDay)
        {
            List<string> stale = new List<string>();
            foreach (string key in pendingDayHediffs.Keys)
            {
                if (DayFromSummaryKey(key) < currentDay)
                {
                    stale.Add(key);
                }
            }

            for (int i = 0; i < stale.Count; i++)
            {
                pendingDayHediffs.Remove(stale[i]);
            }
        }

        /// <summary>Clears all transient day-summary state (on new game / load).</summary>
        private void ResetDaySummaryState()
        {
            pendingDayHediffs.Clear();
            dayStartOpinions.Clear();
            writtenDayReflections.Clear();
            opinionSnapshotDay = -1;
        }

        /// <summary>Day index a stored game tick falls in, aligned with <see cref="CurrentDayIndex"/>.</summary>
        private static int DayIndexForGameTick(int gameTick)
        {
            int offset = Find.TickManager.TicksAbs - Find.TickManager.TicksGame;
            return (gameTick + offset) / GenDate.TicksPerDay;
        }

        private static string DaySummaryKey(string pawnId, int day)
        {
            return pawnId + "|" + day;
        }

        private static int DayFromSummaryKey(string key)
        {
            int sep = key.LastIndexOf('|');
            int day;
            return sep >= 0 && int.TryParse(key.Substring(sep + 1), out day) ? day : int.MaxValue;
        }

        private static string TruncateForEvidence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            const int MaxEvidenceLength = 120;
            string trimmed = text.Trim();
            return trimmed.Length > MaxEvidenceLength ? trimmed.Substring(0, MaxEvidenceLength) + "..." : trimmed;
        }

        private static int DaySummaryMaxHighlights
        {
            get { return Math.Max(1, DiaryTuning.Current.daySummaryMaxHighlights); }
        }

        /// <summary>One major affliction that appeared for a pawn during a day.</summary>
        private struct DayHediffRecord
        {
            public string defName;
            public string label;
            public float weight;
            public bool progressed;
        }

        /// <summary>One candidate moment competing for a place in the day reflection.</summary>
        private struct DaySummarySignal
        {
            public readonly float weight;       // relative selection weight
            public readonly string evidenceLine; // localized prompt cue
            public readonly string contextTag;  // short tag recorded in gameContext

            public DaySummarySignal(float weight, string evidenceLine, string contextTag)
            {
                this.weight = weight;
                this.evidenceLine = evidenceLine;
                this.contextTag = contextTag;
            }
        }
    }
}

// The end-of-day "reflection". The old ambient day note was built only from low-stakes filler
// (small talk, passing thoughts), so it read thin. This file builds a richer memory instead: when a
// colonist beds down, it gathers the day's candidate signals from several collectors — the day's
// major diary events, big opinion swings toward other colonists, newly-appeared afflictions, plus
// the filler as light background when something important happened — then runs a weighted-random
// selection anchored to an important signal and writes one solo reflective entry. It deliberately
// coexists with the per-event entries that already fired in the moment: this is the pawn looking
// back on the whole day.
//
// All state here is transient (cleared on load, re-derived live), like the existing pending notes,
// so this touches no save schema. The opinion baseline is re-snapshotted on load and at each day
// rollover (see DiaryGameComponent.cs); a pawn who sleeps past midnight therefore gets a weaker
// opinion-delta signal that day — an accepted v1 limitation, the other signals still carry it.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Text;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
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

        // "pawnId|quadrumIndex" long reflections already written. Rebuilt from saved events on load,
        // just like the day guard, so reloading during the timing window cannot duplicate one.
        private readonly HashSet<string> writtenQuadrumReflections = new HashSet<string>();

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
            // Try the rare, longer quadrum reflection first. If it emits, skip the ordinary day
            // reflection for this sleep/rest moment so the player and model see only one summary.
            if (TryFlushQuadrumReflectionForPawn(pawn, pawnId, day))
            {
                return;
            }

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
            int importantCandidateCount = CountImportantSignals(candidates);

            // Always release this pawn/day's filler + hediffs so they never separately emit, even
            // when nothing is selected. (Consuming filler also keeps the old fallback path from
            // re-emitting it on day change or save.)
            ConsumePawnDayFiller(pawnId, day);
            pendingDayHediffs.Remove(dayKey);

            List<DaySummarySignal> highlights = SelectHighlights(candidates, DaySummaryMaxHighlights);
            EnsureImportantHighlight(highlights, candidates);
            highlights.Sort((a, b) => b.weight.CompareTo(a.weight));

            StringBuilder tags = new StringBuilder();
            for (int i = 0; i < highlights.Count; i++)
            {
                if (tags.Length > 0)
                {
                    tags.Append(", ");
                }

                tags.Append(highlights[i].contextTag);
            }

            DayReflectionEventData data = new DayReflectionEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = DayReflectionEventData.DefNameToken,
                Day = day,
                CandidateCount = candidates.Count,
                ImportantCandidateCount = importantCandidateCount,
                HighlightCount = highlights.Count,
                FillerMomentCount = fillerCount,
                SignalTags = tags.ToString(),
                AlreadyWritten = false,
            };
            string label = "PawnDiary.Event.DayReflectionLabel".Translate().Resolve();
            string text = BuildDayReflectionText(pawn, highlights);
            string instruction = "PawnDiary.Event.DayReflectionInstruction".Translate(pawn.LabelShortCap).Resolve();
            string gameContext = DayReflectionEventData.BuildGameContext(
                data.Day, data.HighlightCount, data.CandidateCount, data.FillerMomentCount, data.SignalTags);

            // Dispatch through the bus; record this pawn/day as written only if the catalog actually
            // emitted the reflection — the same coupling the old inline Decide + writtenDayReflections had.
            if (Dispatch(new DayReflectionSignal(data, pawn, label, text, instruction, gameContext)))
            {
                writtenDayReflections.Add(dayKey);
            }
        }

        /// <summary>
        /// Writes the rare long quadrum reflection when this pawn's spread-out timing window has
        /// opened and the quadrum contains enough important entries. Returns true only when the
        /// reflection actually emitted; callers use that to skip the ordinary day reflection.
        /// </summary>
        private bool TryFlushQuadrumReflectionForPawn(Pawn pawn, string pawnId, int day)
        {
            if (pawn == null
                || string.IsNullOrWhiteSpace(pawnId)
                || !DiaryTuning.Current.quadrumReflectionEnabled)
            {
                return false;
            }

            int daysPerQuadrum = GenDate.DaysPerQuadrum;
            int quadrum = QuadrumIndexForDay(day);
            int dayInQuadrum = DayInQuadrum(day);
            int timingWindowDays = DiaryTuning.QuadrumReflectionTimingWindowDays;
            if (!QuadrumReflectionPolicy.IsDueForPawn(pawnId, quadrum, dayInQuadrum,
                daysPerQuadrum, timingWindowDays))
            {
                return false;
            }

            string quadrumKey = QuadrumSummaryKey(pawnId, quadrum);
            if (writtenQuadrumReflections.Contains(quadrumKey))
            {
                return false;
            }

            int quadrumStartDay = QuadrumStartDay(quadrum);
            int evidenceEndDay = Math.Min(day, quadrumStartDay + daysPerQuadrum - 1);
            List<QuadrumReflectionSignal> candidates = new List<QuadrumReflectionSignal>();
            CollectQuadrumReflectionSignals(pawnId, quadrumStartDay, evidenceEndDay, candidates);
            if (!QuadrumReflectionPolicy.HasEnoughHighValueEntries(candidates.Count,
                DiaryTuning.QuadrumReflectionMinImportantEntries))
            {
                return false;
            }

            List<QuadrumReflectionSignal> highlights =
                SelectQuadrumHighlights(candidates, QuadrumReflectionMaxPromptEvents);
            if (highlights.Count == 0)
            {
                return false;
            }

            highlights.Sort((left, right) => left.tick.CompareTo(right.tick));
            string signalTags = QuadrumSignalTags(highlights);
            string quadrumDates = QuadrumDateRangeText(quadrumStartDay, evidenceEndDay);
            int dueDay = quadrumStartDay + QuadrumReflectionPolicy.DueDayInQuadrum(
                pawnId, quadrum, daysPerQuadrum, timingWindowDays);

            DayReflectionEventData data = new DayReflectionEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                DefName = DayReflectionEventData.QuadrumDefNameToken,
                Day = day,
                CandidateCount = candidates.Count,
                ImportantCandidateCount = candidates.Count,
                HighlightCount = highlights.Count,
                FillerMomentCount = 0,
                SignalTags = signalTags,
                AlreadyWritten = false,
            };
            string label = "PawnDiary.Event.QuadrumReflectionLabel".Translate().Resolve();
            string text = BuildQuadrumReflectionText(pawn, quadrumDates, highlights);
            string instruction = "PawnDiary.Event.QuadrumReflectionInstruction"
                .Translate(pawn.LabelShortCap, quadrumDates).Resolve();
            string gameContext = DayReflectionEventData.BuildQuadrumGameContext(
                data.Day,
                quadrum,
                quadrumStartDay,
                evidenceEndDay,
                quadrumDates,
                dueDay,
                data.HighlightCount,
                data.CandidateCount,
                data.SignalTags);

            if (Dispatch(new DayReflectionSignal(data, pawn, label, text, instruction, gameContext)))
            {
                writtenQuadrumReflections.Add(quadrumKey);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Finds this pawn's important entries inside the current quadrum. Ordinary day/quadrum
        /// reflections are excluded so summaries never summarize summaries.
        /// </summary>
        private void CollectQuadrumReflectionSignals(string pawnId, int startDay, int endDay,
            List<QuadrumReflectionSignal> candidates)
        {
            IReadOnlyList<DiaryEvent> allEvents = events.AllEvents;
            for (int i = allEvents.Count - 1; i >= 0; i--)
            {
                DiaryEvent ev = allEvents[i];
                if (ev == null)
                {
                    continue;
                }

                int eventDay = DayIndexForGameTick(ev.tick);
                if (eventDay > endDay)
                {
                    continue;
                }

                if (eventDay < startDay)
                {
                    break;
                }

                if (IsReflectionDefName(ev.interactionDefName) || !ev.IsImportant())
                {
                    continue;
                }

                string role;
                if (!ev.TryGetDisplayRoleForPawn(pawnId, out role))
                {
                    continue;
                }

                string line = QuadrumEventEvidenceLine(ev, role);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                float weight = ev.IsCombatRelated()
                    ? DiaryTuning.Current.daySummaryWeightCriticalEvent
                    : DiaryTuning.Current.daySummaryWeightMajorEvent;
                candidates.Add(new QuadrumReflectionSignal(
                    weight,
                    ev.tick,
                    line,
                    DaySummarySignalTag(DayReflectionEventData.SignalKindEvent, ev.interactionDefName)));
            }
        }

        /// <summary>
        /// Adds one signal per important diary event the pawn took part in today. Filler/ambient
        /// entries (not "important") and the reflection's own def are skipped.
        /// </summary>
        private void CollectEventSignals(string pawnId, int day, List<DaySummarySignal> candidates)
        {
            DiaryTuningDef tuning = DiaryTuning.Current;

            // The active scan window is appended in tick order, so scan newest-first and stop once we
            // drop below the target day. Archive pages are not day-reflection evidence anymore.
            IReadOnlyList<DiaryEvent> allEvents = ActiveScanEvents();
            for (int i = allEvents.Count - 1; i >= 0; i--)
            {
                DiaryEvent ev = allEvents[i];
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

                if (IsReflectionDefName(ev.interactionDefName))
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
                string kind = DayReflectionEventData.SignalKindEvent;
                candidates.Add(new DaySummarySignal(
                    weight,
                    line,
                    DaySummarySignalTag(kind, ev.interactionDefName),
                    IsDaySummarySignalImportant(kind)));
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
                string kind = DayReflectionEventData.SignalKindOpinion;
                candidates.Add(new DaySummarySignal(
                    weight,
                    line,
                    DaySummarySignalTag(kind, (delta > 0 ? "+" : "") + delta),
                    IsDaySummarySignalImportant(kind)));
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
                string kind = DayReflectionEventData.SignalKindHediff;
                candidates.Add(new DaySummarySignal(
                    weight,
                    line,
                    DaySummarySignalTag(kind, list[i].defName),
                    IsDaySummarySignalImportant(kind)));
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
                string kind = DayReflectionEventData.SignalKindFiller;
                candidates.Add(new DaySummarySignal(
                    DiaryTuning.Current.daySummaryWeightFiller,
                    line,
                    DaySummarySignalTag(kind, fillerCount.ToString()),
                    IsDaySummarySignalImportant(kind)));
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
        /// Counts the meaningful signals that are strong enough to justify a reflection by themselves.
        /// Filler can color a reflection, but it should not be the reason one exists.
        /// </summary>
        private static int CountImportantSignals(List<DaySummarySignal> signals)
        {
            if (signals == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < signals.Count; i++)
            {
                if (signals[i].important)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// The selection is weighted for variety, but a valid reflection must mention at least one
        /// important signal. If the random draw picked only filler, replace the lightest filler cue
        /// with the strongest important candidate.
        /// </summary>
        private static void EnsureImportantHighlight(List<DaySummarySignal> highlights, List<DaySummarySignal> candidates)
        {
            if (CountImportantSignals(highlights) > 0 || candidates == null)
            {
                return;
            }

            bool foundImportant = false;
            DaySummarySignal strongestImportant = default(DaySummarySignal);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidates[i].important)
                {
                    continue;
                }

                if (!foundImportant || candidates[i].weight > strongestImportant.weight)
                {
                    strongestImportant = candidates[i];
                    foundImportant = true;
                }
            }

            if (!foundImportant)
            {
                return;
            }

            if (highlights == null)
            {
                return;
            }

            if (highlights.Count == 0)
            {
                highlights.Add(strongestImportant);
                return;
            }

            int replaceIndex = 0;
            float replaceWeight = highlights[0].weight;
            for (int i = 1; i < highlights.Count; i++)
            {
                if (highlights[i].weight < replaceWeight)
                {
                    replaceIndex = i;
                    replaceWeight = highlights[i].weight;
                }
            }

            highlights[replaceIndex] = strongestImportant;
        }

        /// <summary>
        /// XML-backed policy check for whether a day-reflection signal kind can create a reflection.
        /// </summary>
        private static bool IsDaySummarySignalImportant(string signalKind)
        {
            return DayReflectionEventData.IsImportantSignalKind(
                signalKind,
                DiaryTuning.Current.daySummaryImportantSignalKinds);
        }

        /// <summary>
        /// Stable tag format embedded in gameContext for diagnostics and prompt context.
        /// </summary>
        private static string DaySummarySignalTag(string signalKind, string detail)
        {
            return signalKind + ":" + (detail ?? string.Empty);
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
            string label = DiaryLineCleaner.CleanLine(ev.interactionLabel);
            string body = DiaryLineCleaner.CleanLine(ev.DisplayTextForRole(role));
            body = TruncateForEvidence(body);

            if (string.IsNullOrWhiteSpace(body))
            {
                return label;
            }

            return string.IsNullOrWhiteSpace(label) ? body : label + " — " + body;
        }

        /// <summary>
        /// Dated evidence cue for a long quadrum reflection. The prompt intentionally receives only
        /// the selected few highlights, not every important event in the quadrum.
        /// </summary>
        private static string QuadrumEventEvidenceLine(DiaryEvent ev, string role)
        {
            string line = EventEvidenceLine(ev, role);
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            string date = DiaryLineCleaner.CleanLine(ev.date);
            return string.IsNullOrWhiteSpace(date)
                ? line
                : "PawnDiary.Event.QuadrumReflectionEvidenceLine".Translate(date, line).Resolve();
        }

        private static string BuildQuadrumReflectionText(Pawn pawn, string quadrumDates,
            List<QuadrumReflectionSignal> highlights)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("PawnDiary.Event.QuadrumReflectionHeader"
                .Translate(pawn.LabelShortCap, quadrumDates).Resolve());
            for (int i = 0; i < highlights.Count; i++)
            {
                builder.Append("\n").Append("- ").Append(highlights[i].evidenceLine);
            }

            return builder.ToString();
        }

        private static List<QuadrumReflectionSignal> SelectQuadrumHighlights(
            List<QuadrumReflectionSignal> candidates, int max)
        {
            List<QuadrumReflectionSignal> pool = new List<QuadrumReflectionSignal>(candidates);
            List<QuadrumReflectionSignal> chosen = new List<QuadrumReflectionSignal>();
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

        private static string QuadrumSignalTags(List<QuadrumReflectionSignal> highlights)
        {
            StringBuilder tags = new StringBuilder();
            for (int i = 0; i < highlights.Count; i++)
            {
                if (tags.Length > 0)
                {
                    tags.Append(", ");
                }

                tags.Append(highlights[i].contextTag);
            }

            return tags.ToString();
        }

        private static string QuadrumDateRangeText(int startDay, int endDay)
        {
            return "PawnDiary.Event.QuadrumReflectionDateRange"
                .Translate(DateStringForDay(startDay), DateStringForDay(endDay)).Resolve();
        }

        private static string DateStringForDay(int absoluteDay)
        {
            int currentDay = CurrentDayIndex;
            int tickOffset = (absoluteDay - currentDay) * GenDate.TicksPerDay;
            return GenDate.DateFullStringAt(Find.TickManager.TicksAbs + tickOffset, Vector2.zero);
        }

        private static bool IsReflectionDefName(string defName)
        {
            return string.Equals(defName, DayReflectionEventData.DefNameToken, StringComparison.OrdinalIgnoreCase)
                || string.Equals(defName, DayReflectionEventData.QuadrumDefNameToken, StringComparison.OrdinalIgnoreCase);
        }

        private static int QuadrumIndexForDay(int day)
        {
            return day / GenDate.DaysPerQuadrum;
        }

        private static int DayInQuadrum(int day)
        {
            int days = GenDate.DaysPerQuadrum;
            int value = day % days;
            return value < 0 ? value + days : value;
        }

        private static int QuadrumStartDay(int quadrum)
        {
            return quadrum * GenDate.DaysPerQuadrum;
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
            writtenQuadrumReflections.Clear();
            opinionSnapshotDay = -1;
        }

        /// <summary>
        /// Rebuilds the transient once-per-pawn/day guard from hot saved DayReflection events after load.
        /// Without this, a pawn that saved while resting could write the same reflection again after
        /// loading because the guard itself is intentionally not part of the save schema.
        /// </summary>
        private void RebuildWrittenDayReflectionsFromEvents()
        {
            writtenDayReflections.Clear();
            writtenQuadrumReflections.Clear();
            IReadOnlyList<DiaryEvent> allEvents = ActiveScanEvents();
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent ev = allEvents[i];
                if (ev == null || string.IsNullOrWhiteSpace(ev.initiatorPawnId))
                {
                    continue;
                }

                if (string.Equals(ev.interactionDefName, DayReflectionEventData.DefNameToken, StringComparison.OrdinalIgnoreCase))
                {
                    writtenDayReflections.Add(DaySummaryKey(ev.initiatorPawnId, DayIndexForGameTick(ev.tick)));
                }
                else if (string.Equals(ev.interactionDefName, DayReflectionEventData.QuadrumDefNameToken, StringComparison.OrdinalIgnoreCase))
                {
                    writtenQuadrumReflections.Add(QuadrumSummaryKey(ev.initiatorPawnId, QuadrumIndexForDay(DayIndexForGameTick(ev.tick))));
                }
            }
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
            return trimmed.Length > MaxEvidenceLength
                ? TextTruncation.SafePrefix(trimmed, MaxEvidenceLength) + "..."
                : trimmed;
        }

        private static int DaySummaryMaxHighlights
        {
            get { return Math.Max(1, DiaryTuning.Current.daySummaryMaxHighlights); }
        }

        private static int QuadrumReflectionMaxPromptEvents
        {
            get { return Math.Max(1, DiaryTuning.Current.quadrumReflectionMaxPromptEvents); }
        }

        private static string QuadrumSummaryKey(string pawnId, int quadrum)
        {
            return pawnId + "|" + quadrum;
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
            public readonly bool important;     // true when this signal can justify a reflection

            public DaySummarySignal(float weight, string evidenceLine, string contextTag, bool important)
            {
                this.weight = weight;
                this.evidenceLine = evidenceLine;
                this.contextTag = contextTag;
                this.important = important;
            }
        }

        /// <summary>One dated high-value diary entry competing for a quadrum reflection prompt slot.</summary>
        private struct QuadrumReflectionSignal
        {
            public readonly float weight;
            public readonly int tick;
            public readonly string evidenceLine;
            public readonly string contextTag;

            public QuadrumReflectionSignal(float weight, int tick, string evidenceLine, string contextTag)
            {
                this.weight = weight;
                this.tick = tick;
                this.evidenceLine = evidenceLine;
                this.contextTag = contextTag;
            }
        }
    }
}

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

        // Quality Wave B6 pacing rows: one per pawn per day, holding that pawn's daily low-salience
        // page count and the small moments the soft cap folded away. Unlike the hediff/filler stores
        // above this one IS saved, because reloading mid-day must not hand a pawn a fresh allowance.
        //
        // The list is the save schema (LookMode.Deep, like activeEventWindows); the dictionary is a
        // transient "pawnId|dayIndex" index over exactly the same objects, rebuilt after load. Rows
        // for earlier days are dropped at the day rollover, so both stay ~one row per colonist.
        private List<PawnDayDigestState> dayDigestStates = new List<PawnDayDigestState>();
        private readonly Dictionary<string, PawnDayDigestState> pendingDayDigest =
            new Dictionary<string, PawnDayDigestState>();

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
        /// Loaded-fixture-compatible sleep-path seam. Production rest scans call the same unified
        /// arbitration directly; a selected daily candidate still uses this file's unchanged collectors,
        /// weighted highlight selection, text, and signal.
        /// </summary>
        private void FlushDaySummaryForPawn(Pawn pawn)
        {
            ArbitrateReflectionsForPawn(pawn);
        }

        /// <summary>
        /// Collects the ordinary daily reflection without consuming its filler/hediff batches. The
        /// selected runtime candidate acknowledges those batches only after Dispatch succeeds.
        /// </summary>
        private ReflectionRuntimeCandidate PrepareDayReflectionCandidate(
            Pawn pawn,
            string pawnId,
            int day,
            bool collectEvidence)
        {
            string dayKey = DaySummaryKey(pawnId, day);
            bool alreadyWritten = writtenDayReflections.Contains(dayKey);
            bool groupEnabled = DiaryTuning.Current.daySummaryEnabled
                && IsReflectionGroupEnabled(DayReflectionEventData.DefNameToken);
            ReflectionRuntimeCandidate runtime = new ReflectionRuntimeCandidate
            {
                opportunity = new ReflectionOpportunity
                {
                    kind = NarrativeReflectionKindTokens.Day,
                    pawnId = pawnId,
                    nowTick = Find.TickManager.TicksGame,
                    candidateMemoryCount = 1,
                    importance = NarrativeSalienceTokens.Minor,
                    due = !alreadyWritten,
                    alreadyWritten = alreadyWritten,
                    cooldownSatisfied = true,
                    groupEnabled = groupEnabled
                }
            };
            if (alreadyWritten)
            {
                return runtime;
            }

            runtime.advanceDisabledDebt = () =>
            {
                writtenDayReflections.Add(dayKey);
                // The day cadence is bounded, but ambient notes stay available to the documented
                // daySummaryEnabled=false fallback (and to the shared-policy-disabled fallback).
                pendingDayHediffs.Remove(dayKey);
                ClearDayDigestLines(dayKey);
            };
            if (!groupEnabled)
            {
                return runtime;
            }

            if (!collectEvidence)
            {
                return runtime;
            }

            // Gather candidates from every source, then keep only the most important few.
            List<DaySummarySignal> candidates = new List<DaySummarySignal>();
            CollectEventSignals(pawnId, day, candidates);
            int nowTick = Find.TickManager.TicksGame;
            int dayStartTick = GameTickForDayIndex(day);
            int newsTimeout = Math.Max(0, DiaryContextReactions.TimeoutTicks(
                DiaryContextReactions.ColonyNews,
                GenDate.TicksPerDay));
            if (newsTimeout > 0)
            {
                dayStartTick = Math.Max(dayStartTick, nowTick - newsTimeout);
            }
            CollectNewsSignals(pawn, dayStartTick, nowTick, candidates);
            CollectOpinionSignals(pawn, candidates);
            CollectHediffSignals(dayKey, candidates);
            int fillerCount = CollectFillerSignal(pawnId, day, candidates);
            CollectDigestSignals(pawnId, day, candidates);
            int importantCandidateCount = CountImportantSignals(candidates);

            runtime.opportunity.candidateMemoryCount = candidates.Count;
            runtime.opportunity.due = candidates.Count > 0 && importantCandidateCount > 0;
            if (!runtime.opportunity.due)
            {
                // Preserve the old quiet-day behavior: evidence that cannot justify a page is released
                // now, while a selected page's evidence remains strictly success-acknowledged below.
                runtime.settleIneligible = () =>
                {
                    ConsumePawnDayFiller(pawnId, day);
                    pendingDayHediffs.Remove(dayKey);
                    ClearDayDigestLines(dayKey);
                };
                return runtime;
            }

            runtime.dispatch = () => DispatchPreparedDayReflection(
                pawn, pawnId, day, candidates, fillerCount, importantCandidateCount);
            runtime.consumeAfterDispatch = () =>
            {
                ConsumePawnDayFiller(pawnId, day);
                pendingDayHediffs.Remove(dayKey);
                ClearDayDigestLines(dayKey);
                writtenDayReflections.Add(dayKey);
            };
            return runtime;
        }

        private bool DispatchPreparedDayReflection(
            Pawn pawn,
            string pawnId,
            int day,
            List<DaySummarySignal> candidates,
            int fillerCount,
            int importantCandidateCount)
        {
            List<DaySummarySignal> highlights =
                SelectHighlightsImportantFirst(candidates, DaySummaryMaxHighlights);
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

            return Dispatch(new DayReflectionSignal(data, pawn, label, text, instruction, gameContext));
        }

        /// <summary>Collects one detached quadrum opportunity without advancing its once-per-window guard.</summary>
        private ReflectionRuntimeCandidate PrepareQuadrumReflectionCandidate(
            Pawn pawn,
            string pawnId,
            int day,
            bool collectEvidence)
        {
            int daysPerQuadrum = GenDate.DaysPerQuadrum;
            int quadrum = QuadrumIndexForDay(day);
            int dayInQuadrum = DayInQuadrum(day);
            int timingWindowDays = DiaryTuning.QuadrumReflectionTimingWindowDays;
            bool cadenceDue = QuadrumReflectionPolicy.IsDueForPawn(
                pawnId, quadrum, dayInQuadrum, daysPerQuadrum, timingWindowDays);
            string quadrumKey = QuadrumSummaryKey(pawnId, quadrum);
            bool alreadyWritten = writtenQuadrumReflections.Contains(quadrumKey);
            bool groupEnabled = DiaryTuning.Current.daySummaryEnabled
                && DiaryTuning.Current.quadrumReflectionEnabled
                && IsReflectionGroupEnabled(DayReflectionEventData.QuadrumDefNameToken);
            ReflectionRuntimeCandidate runtime = new ReflectionRuntimeCandidate
            {
                opportunity = new ReflectionOpportunity
                {
                    kind = NarrativeReflectionKindTokens.Quadrum,
                    pawnId = pawnId,
                    nowTick = Find.TickManager.TicksGame,
                    candidateMemoryCount = 1,
                    importance = NarrativeSalienceTokens.Meaningful,
                    due = cadenceDue && !alreadyWritten,
                    alreadyWritten = alreadyWritten,
                    cooldownSatisfied = true,
                    groupEnabled = groupEnabled
                }
            };
            if (!runtime.opportunity.due)
            {
                return runtime;
            }

            runtime.advanceDisabledDebt = () => writtenQuadrumReflections.Add(quadrumKey);
            if (!groupEnabled)
            {
                return runtime;
            }

            if (!collectEvidence)
            {
                return runtime;
            }

            int quadrumStartDay = QuadrumStartDay(quadrum);
            int evidenceEndDay = Math.Min(day, quadrumStartDay + daysPerQuadrum - 1);
            List<QuadrumReflectionSignal> candidates = new List<QuadrumReflectionSignal>();
            CollectQuadrumReflectionSignals(pawnId, quadrumStartDay, evidenceEndDay, candidates);
            int currentImportantEntryCount = candidates.Count;
            runtime.opportunity.candidateMemoryCount = currentImportantEntryCount;
            if (!QuadrumReflectionPolicy.HasEnoughHighValueEntries(currentImportantEntryCount,
                DiaryTuning.QuadrumReflectionMinImportantEntries))
            {
                runtime.opportunity.due = false;
                return runtime;
            }

            // H3 news and H5's prior-year callback enrich an already-earned quadrum reflection. They
            // must never lower the current-quadrum importance gate or inflate coordinator arbitration.
            // Waiting until after the gate also avoids repeating the year-back hot/archive scan every
            // 250 ticks while a quiet pawn rests inside the multi-day timing window.
            CollectLastYearQuadrumMemory(pawnId, quadrum, candidates);
            CollectNewsSignals(
                pawn,
                GameTickForDayIndex(quadrumStartDay),
                Math.Min(Find.TickManager.TicksGame, GameTickForDayIndex(evidenceEndDay + 1) - 1),
                candidates);
            runtime.dispatch = () => DispatchPreparedQuadrumReflection(
                pawn,
                pawnId,
                day,
                quadrum,
                quadrumStartDay,
                evidenceEndDay,
                daysPerQuadrum,
                timingWindowDays,
                currentImportantEntryCount,
                candidates);
            runtime.consumeAfterDispatch = () => writtenQuadrumReflections.Add(quadrumKey);
            return runtime;
        }

        private bool DispatchPreparedQuadrumReflection(
            Pawn pawn,
            string pawnId,
            int day,
            int quadrum,
            int quadrumStartDay,
            int evidenceEndDay,
            int daysPerQuadrum,
            int timingWindowDays,
            int currentImportantEntryCount,
            List<QuadrumReflectionSignal> candidates)
        {
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
                ImportantCandidateCount = currentImportantEntryCount,
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
                data.ImportantCandidateCount,
                data.SignalTags);

            return Dispatch(new DayReflectionSignal(data, pawn, label, text, instruction, gameContext));
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
        /// Adds at most one important page from the same quadrum one year earlier. The callback has a
        /// low fixed final weight so it enriches the current reflection without crowding out current
        /// events; source strength is used only to choose which prior-year page represents the season.
        /// </summary>
        internal void CollectLastYearQuadrumMemory(
            string pawnId,
            int currentQuadrum,
            List<QuadrumReflectionSignal> candidates)
        {
            if (!DiaryTuning.Current.onThisDayQuadrumCallbackEnabled
                || !QuadrumAnniversaryMemoryPolicy.HasPreviousYear(currentQuadrum))
            {
                return;
            }

            QuadrumAnniversaryMemoryCandidate memory =
                FindLastYearQuadrumMemory(pawnId, currentQuadrum);
            if (memory == null)
            {
                return;
            }

            string framedLine = "PawnDiary.Event.QuadrumReflectionLastYear"
                .Translate(memory.evidenceLine).Resolve();
            candidates.Add(new QuadrumReflectionSignal(
                DiaryTuning.Current.daySummaryWeightMajorEvent * 0.5f,
                memory.tick,
                framedLine,
                DaySummarySignalTag(DayReflectionEventData.SignalKindMemory, memory.sourceIdentity)));
        }

        /// <summary>
        /// Collects matching prior-year candidates from hot and compact archive storage, then delegates
        /// identity deduplication and maximum-weight selection to the pure policy. Internal for the
        /// loaded-game fixture; production reaches it only through <see cref="CollectLastYearQuadrumMemory"/>.
        /// </summary>
        internal QuadrumAnniversaryMemoryCandidate FindLastYearQuadrumMemory(
            string pawnId,
            int currentQuadrum)
        {
            if (string.IsNullOrWhiteSpace(pawnId)
                || !QuadrumAnniversaryMemoryPolicy.HasPreviousYear(currentQuadrum))
            {
                return null;
            }

            int targetQuadrum = QuadrumAnniversaryMemoryPolicy.PreviousYearQuadrum(currentQuadrum);
            int startDay = QuadrumStartDay(targetQuadrum);
            int endDay = startDay + GenDate.DaysPerQuadrum - 1;
            List<QuadrumAnniversaryMemoryCandidate> memories =
                new List<QuadrumAnniversaryMemoryCandidate>();

            IReadOnlyList<DiaryEvent> hotEvents = events.AllEvents;
            for (int i = hotEvents.Count - 1; i >= 0; i--)
            {
                DiaryEvent ev = hotEvents[i];
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

                memories.Add(new QuadrumAnniversaryMemoryCandidate(
                    QuadrumAnniversaryMemoryPolicy.IdentityFor(
                        ev.eventId, ev.tick, ev.interactionDefName, role),
                    ev.tick,
                    ev.IsCombatRelated()
                        ? DiaryTuning.Current.daySummaryWeightCriticalEvent
                        : DiaryTuning.Current.daySummaryWeightMajorEvent,
                    line));
            }

            IReadOnlyList<ArchivedDiaryEntry> archivedEntries = archive?.EntriesForPawn(pawnId);
            if (archivedEntries != null)
            {
                for (int i = archivedEntries.Count - 1; i >= 0; i--)
                {
                    ArchivedDiaryEntry entry = archivedEntries[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    int eventDay = DayIndexForGameTick(entry.tick);
                    if (eventDay > endDay)
                    {
                        continue;
                    }

                    if (eventDay < startDay)
                    {
                        break;
                    }

                    if (!entry.important || IsArchivedReflectionMemorySource(entry))
                    {
                        continue;
                    }

                    string line = QuadrumArchiveEvidenceLine(entry);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    memories.Add(new QuadrumAnniversaryMemoryCandidate(
                        QuadrumAnniversaryMemoryPolicy.IdentityFor(
                            entry.eventId, entry.tick, entry.interactionDefName, entry.povRole),
                        entry.tick,
                        IsCriticalArchivedMemory(entry)
                            ? DiaryTuning.Current.daySummaryWeightCriticalEvent
                            : DiaryTuning.Current.daySummaryWeightMajorEvent,
                        line));
                }
            }

            return QuadrumAnniversaryMemoryPolicy.SelectBest(memories);
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
        /// Adds the newest allowed, unsuperseded colony letter inside the pawn's current evidence
        /// window. The lower bound is clipped to the pawn's first arrival page so a new colonist never
        /// remembers colony news from before they joined.
        /// </summary>
        private void CollectNewsSignals(
            Pawn pawn,
            int startTick,
            int endTick,
            List<DaySummarySignal> candidates)
        {
            ColonyNewsSignal news;
            if (!TryCollectNewestNews(pawn, startTick, endTick, out news))
            {
                return;
            }

            string kind = DayReflectionEventData.SignalKindNews;
            candidates.Add(new DaySummarySignal(
                DiaryTuning.Current.daySummaryWeightNews,
                news.evidenceLine,
                DaySummarySignalTag(kind, news.category),
                IsDaySummarySignalImportant(kind)));
        }

        /// <summary>
        /// Quadrum adapter for the same bounded colony-letter scan used by daily reflections.
        /// </summary>
        private void CollectNewsSignals(
            Pawn pawn,
            int startTick,
            int endTick,
            List<QuadrumReflectionSignal> candidates)
        {
            ColonyNewsSignal news;
            if (!TryCollectNewestNews(pawn, startTick, endTick, out news))
            {
                return;
            }

            candidates.Add(new QuadrumReflectionSignal(
                DiaryTuning.Current.daySummaryWeightNews,
                news.tick,
                news.evidenceLine,
                DaySummarySignalTag(DayReflectionEventData.SignalKindNews, news.category)));
        }

        /// <summary>
        /// Scans RimWorld's archive newest-first with the XML cap, then rejects a candidate when a
        /// direct same-category page for this pawn already owns the story in hot or archived history.
        /// </summary>
        private bool TryCollectNewestNews(
            Pawn pawn,
            int requestedStartTick,
            int endTick,
            out ColonyNewsSignal news)
        {
            news = default(ColonyNewsSignal);
            DiaryContextReactionDef policy =
                DiaryContextReactions.ForKey(DiaryContextReactions.ColonyNews);
            if (!policy.enabled || pawn == null || Find.Archive == null || endTick < requestedStartTick)
            {
                return false;
            }

            if (policy.requireHomeMap && (pawn.Map == null || !pawn.Map.IsPlayerHome))
            {
                return false;
            }

            if (policy.requireDanger
                && (pawn.Map?.dangerWatcher == null
                    || pawn.Map.dangerWatcher.DangerRating == StoryDanger.None))
            {
                return false;
            }

            IReadOnlyList<ColonyNewsCategoryRule> rules = policy.newsCategories;
            if (rules == null || rules.Count == 0)
            {
                return false;
            }

            string pawnId = pawn.GetUniqueLoadID();
            int startTick = requestedStartTick;
            int? arrivalTick = FirstArrivalTickFor(pawnId, FindDiary(pawn, false));
            if (arrivalTick.HasValue)
            {
                startTick = Math.Max(startTick, arrivalTick.Value);
            }

            List<IArchivable> archivables = Find.Archive.ArchivablesListForReading;
            if (archivables == null || archivables.Count == 0 || endTick < startTick)
            {
                return false;
            }

            int scanBack = Math.Max(0, DiaryContextReactions.ScanBack(
                DiaryContextReactions.ColonyNews,
                40));
            int scanned = 0;
            for (int i = archivables.Count - 1; i >= 0 && scanned < scanBack; i--, scanned++)
            {
                IArchivable archivable = archivables[i];
                Letter letter = archivable as Letter;
                if (letter?.def == null)
                {
                    continue;
                }

                int createdTick = SafeArchivableCreatedTick(archivable);
                if (createdTick < 0)
                {
                    continue;
                }

                if (createdTick > endTick)
                {
                    continue;
                }

                if (createdTick < startTick)
                {
                    // RimWorld stores archivables in creation order, so every remaining row is older.
                    break;
                }

                string category = ColonyNewsPolicy.CategoryForLetter(letter.def.defName, rules);
                int letterDay = DayIndexForGameTick(createdTick);
                int ownerStartTick = Math.Max(startTick, GameTickForDayIndex(letterDay));
                int ownerEndTick = Math.Min(endTick, GameTickForDayIndex(letterDay + 1) - 1);
                if (string.IsNullOrWhiteSpace(category)
                    || HasDirectNewsOwner(pawnId, category, ownerStartTick, ownerEndTick, rules))
                {
                    continue;
                }

                string label = SafeColonyNewsLabel(archivable);
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                news = new ColonyNewsSignal(
                    category,
                    createdTick,
                    "PawnDiary.Event.DayReflectionNews".Translate(label).Resolve());
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks both repositories for a displayable direct page owned by this pawn, category, and
        /// evidence window. The category is matched only from stable domain/context tokens.
        /// </summary>
        private bool HasDirectNewsOwner(
            string pawnId,
            string category,
            int startTick,
            int endTick,
            IReadOnlyList<ColonyNewsCategoryRule> rules)
        {
            IReadOnlyList<DiaryEvent> hot = events.AllEvents;
            for (int i = hot.Count - 1; i >= 0; i--)
            {
                DiaryEvent diaryEvent = hot[i];
                if (diaryEvent == null || diaryEvent.tick > endTick)
                {
                    continue;
                }

                if (diaryEvent.tick < startTick)
                {
                    break;
                }

                string role;
                if (!diaryEvent.TryGetDisplayRoleForPawn(pawnId, out role))
                {
                    continue;
                }

                string domain = DiaryEventDomainClassifier.DomainForContext(diaryEvent.gameContext);
                if (ColonyNewsPolicy.EventOwnsCategory(
                    category,
                    domain,
                    diaryEvent.gameContext,
                    rules))
                {
                    return true;
                }
            }

            IReadOnlyList<ArchivedDiaryEntry> archived = archive.EntriesForPawn(pawnId);
            for (int i = archived.Count - 1; i >= 0; i--)
            {
                ArchivedDiaryEntry entry = archived[i];
                if (entry == null || entry.tick > endTick)
                {
                    continue;
                }

                if (entry.tick < startTick)
                {
                    break;
                }

                string context = entry.decorationGameContext;
                string domain = string.IsNullOrWhiteSpace(entry.decorationDomain)
                    ? DiaryEventDomainClassifier.DomainForContext(context)
                    : entry.decorationDomain;
                if (ColonyNewsPolicy.EventOwnsCategory(category, domain, context, rules))
                {
                    return true;
                }
            }

            return false;
        }

        private static int SafeArchivableCreatedTick(IArchivable archivable)
        {
            if (archivable == null)
            {
                return -1;
            }

            try
            {
                return archivable.CreatedTicksGame;
            }
            catch
            {
                return -1;
            }
        }

        private static string SafeColonyNewsLabel(IArchivable archivable)
        {
            if (archivable == null)
            {
                return string.Empty;
            }

            try
            {
                return PromptTextSanitizer.LocalizedPromptText(archivable.ArchivedLabel);
            }
            catch
            {
                return string.Empty;
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

                int current;
                if (!TryReadOpinion(pawn, other, out current))
                {
                    continue;
                }

                int delta = current - baseline;
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
        /// Quality Wave B6. Adds one low-weight candidate per moment the daily soft cap folded away
        /// for this pawn today, so a quiet-but-not-empty day still reads as lived-in.
        ///
        /// These candidates are ALWAYS non-important, whatever the XML important-kind list says: a
        /// digest line exists precisely because its own page was judged not worth writing, so it may
        /// colour a reflection that already earned itself but can never create one.
        /// </summary>
        private void CollectDigestSignals(string pawnId, int day, List<DaySummarySignal> candidates)
        {
            PawnDayDigestState state;
            if (!pendingDayDigest.TryGetValue(DaySummaryKey(pawnId, day), out state)
                || state?.lines == null)
            {
                return;
            }

            for (int i = 0; i < state.lines.Count; i++)
            {
                DayDigestRecord record = state.lines[i];
                string line = TruncateForEvidence(record?.line);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                candidates.Add(new DaySummarySignal(
                    DiaryTuning.Current.daySummaryWeightDigest,
                    line,
                    DaySummarySignalTag(DayReflectionEventData.SignalKindDigest, record.sourceKind),
                    false));
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
        /// Quality Wave B6. Runs the weighted rotation in two passes so priority is explicit: the
        /// important evidence that earned this reflection selects first, and only the slots it leaves
        /// over are offered to the background pool (news, filler, digest). Without this split, adding
        /// up to four digest candidates would statistically crowd out the real story of the day.
        /// </summary>
        private static List<DaySummarySignal> SelectHighlightsImportantFirst(
            List<DaySummarySignal> candidates, int max)
        {
            List<DaySummarySignal> important = new List<DaySummarySignal>();
            List<DaySummarySignal> background = new List<DaySummarySignal>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].important)
                {
                    important.Add(candidates[i]);
                }
                else
                {
                    background.Add(candidates[i]);
                }
            }

            List<DaySummarySignal> chosen = SelectHighlights(
                important, DigestPacingPolicy.ImportantSelectionSlots(important.Count, max));
            int remaining = DigestPacingPolicy.RemainingSelectionSlots(chosen.Count, max);
            if (remaining > 0)
            {
                chosen.AddRange(SelectHighlights(background, remaining));
            }

            return chosen;
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

            // Highlight rotation is frozen into the captured reflection event. Use a private
            // one-shot stream so this cosmetic sampling cannot perturb RimWorld's gameplay RNG.
            Rand.PushState();
            try
            {
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
            }
            finally
            {
                Rand.PopState();
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

        /// <summary>
        /// Dated evidence cue reconstructed from one compact archive row. Prefer generated prose, but
        /// keep the saved fallback fact useful for failed/stale historical pages.
        /// </summary>
        private static string QuadrumArchiveEvidenceLine(ArchivedDiaryEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            string label = DiaryLineCleaner.CleanLine(entry.interactionLabel);
            string bodySource = entry.archivedGenerationStale
                ? entry.text
                : (string.IsNullOrWhiteSpace(entry.generatedText) ? entry.text : entry.generatedText);
            string body = DiaryLineCleaner.CleanLine(bodySource);
            body = TruncateForEvidence(body);
            string line = string.IsNullOrWhiteSpace(body)
                ? label
                : (string.IsNullOrWhiteSpace(label) ? body : label + " — " + body);
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            string date = DiaryLineCleaner.CleanLine(entry.date);
            return string.IsNullOrWhiteSpace(date)
                ? line
                : "PawnDiary.Event.QuadrumReflectionEvidenceLine".Translate(date, line).Resolve();
        }

        /// <summary>
        /// Recovers the current hot-event critical/major split from metadata retained in cold storage.
        /// These are stable saved strings, so old or no-DLC rows simply fall through to major weight.
        /// </summary>
        private static bool IsCriticalArchivedMemory(ArchivedDiaryEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            string domain = ArchivedMemoryDomain(entry);
            return string.Equals(domain, DiaryEventDomainClassifier.Raid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(domain, DiaryEventDomainClassifier.MentalState, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.colorCue, DiaryEvent.CombatColorCue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.colorCue, DiaryEvent.SocialFightColorCue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.colorCue, DiaryEvent.MentalBreakColorCue, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when an archived row is itself a summary/reflection rather than source evidence.</summary>
        private static bool IsArchivedReflectionMemorySource(ArchivedDiaryEntry entry)
        {
            return entry != null
                && (IsReflectionDefName(entry.interactionDefName)
                    || string.Equals(
                        ArchivedMemoryDomain(entry),
                        DiaryEventDomainClassifier.Reflection,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static string ArchivedMemoryDomain(ArchivedDiaryEntry entry)
        {
            return entry == null
                ? string.Empty
                : (string.IsNullOrWhiteSpace(entry.decorationDomain)
                    ? DiaryEventDomainClassifier.DomainForContext(entry.decorationGameContext)
                    : entry.decorationDomain);
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
            // As with day highlights, the chosen set is persisted; isolate its one-shot weighted
            // sampling from the seeded RNG stream used by RimWorld's simulation.
            Rand.PushState();
            try
            {
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
            }
            finally
            {
                Rand.PopState();
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
                || string.Equals(defName, DayReflectionEventData.QuadrumDefNameToken, StringComparison.OrdinalIgnoreCase)
                || string.Equals(defName, ArcReflectionEventData.DefNameToken, StringComparison.OrdinalIgnoreCase)
                || string.Equals(defName, BeliefReflectionEventData.DefNameToken, StringComparison.OrdinalIgnoreCase);
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

                    int opinion;
                    if (TryReadOpinion(from, to, out opinion))
                    {
                        dayStartOpinions[fromId + "|" + to.GetUniqueLoadID()] = opinion;
                    }
                }
            }

            opinionSnapshotDay = CurrentDayIndex;
            PruneStaleDayHediffs(CurrentDayIndex);
            // Same rollover boundary for B6: yesterday's pacing counts and unused digest lines go.
            PruneStaleDayDigest(CurrentDayIndex);
        }

        // ── Quality Wave B6: daily pacing store ───────────────────────────────────────────────────
        // Everything below reads or writes the saved pawn/day rows. The DECISIONS live in the pure
        // DigestPacingPolicy; these methods only own the live lookup, the localized line, and the
        // save/rollover bookkeeping.

        /// <summary>
        /// How many low-salience pages this pawn has already written on this day. A pawn with no row
        /// yet has written none, which is exactly what an old save should report.
        /// </summary>
        internal int LowSalienceCountForDay(string pawnId, int day)
        {
            PawnDayDigestState state;
            return pendingDayDigest.TryGetValue(DaySummaryKey(pawnId, day), out state) && state != null
                ? state.lowSalienceCount
                : 0;
        }

        /// <summary>
        /// This pawn's saved pacing row for a day, or null when none exists yet. Production goes
        /// through the count/line accessors around this one; this is the read seam the loaded-game
        /// tests use to inspect a row directly.
        /// </summary>
        internal PawnDayDigestState DayDigestStateFor(string pawnId, int day)
        {
            PawnDayDigestState state;
            return pendingDayDigest.TryGetValue(DaySummaryKey(pawnId, day), out state) ? state : null;
        }

        /// <summary>
        /// Records that a low-salience page really was written. Called only AFTER a successful emit,
        /// so a page the catalog or a source dropped late never consumes the pawn's daily allowance.
        /// </summary>
        internal void RecordLowSalienceEmission(string pawnId, int day)
        {
            PawnDayDigestState state = EnsureDayDigestState(pawnId, day);
            if (state != null)
            {
                state.lowSalienceCount = DigestPacingPolicy.NextEmittedCount(state.lowSalienceCount);
            }
        }

        /// <summary>
        /// Remembers one moment the soft cap folded away, so the evening reflection can still mention
        /// it. The pure policy owns duplicate rejection and the newest-wins eviction.
        /// </summary>
        internal void AddDayDigestLine(string pawnId, int day, string sourceKind, string line, int tick)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            PawnDayDigestState state = EnsureDayDigestState(pawnId, day);
            if (state == null)
            {
                return;
            }

            List<DigestLineCandidate> buffer = DigestBuffer(state);
            if (DigestPacingPolicy.AddLine(
                buffer,
                new DigestLineCandidate { tick = tick, sourceKind = sourceKind, line = line },
                DiaryTuning.Current.dayDigestMaxLines))
            {
                CopyDigestBufferBack(state, buffer);
            }
        }

        /// <summary>
        /// Drops this pawn/day's buffered moments once the reflection has consumed (or declined) them,
        /// mirroring the filler and hediff release paths. The pacing COUNT deliberately survives: the
        /// day is not over, and a pawn who already wrote its quota keeps that quota after bedtime.
        /// </summary>
        private void ClearDayDigestLines(string dayKey)
        {
            PawnDayDigestState state;
            if (pendingDayDigest.TryGetValue(dayKey, out state))
            {
                state?.ClearLines();
            }
        }

        private PawnDayDigestState EnsureDayDigestState(string pawnId, int day)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            string key = DaySummaryKey(pawnId, day);
            PawnDayDigestState state;
            if (pendingDayDigest.TryGetValue(key, out state) && state != null)
            {
                return state;
            }

            state = new PawnDayDigestState { pawnId = pawnId, day = day };
            state.Normalize();
            dayDigestStates.Add(state);
            pendingDayDigest[key] = state;
            return state;
        }

        // DigestPacingPolicy is pure, so it works on plain DigestLineCandidate rows rather than on the
        // IExposable save rows. These two helpers are the (small, bounded) translation both ways.
        private static List<DigestLineCandidate> DigestBuffer(PawnDayDigestState state)
        {
            List<DigestLineCandidate> buffer = new List<DigestLineCandidate>(state.lines.Count + 1);
            for (int i = 0; i < state.lines.Count; i++)
            {
                DayDigestRecord record = state.lines[i];
                if (record != null)
                {
                    buffer.Add(new DigestLineCandidate
                    {
                        tick = record.tick,
                        sourceKind = record.sourceKind,
                        line = record.line
                    });
                }
            }

            return buffer;
        }

        private static void CopyDigestBufferBack(
            PawnDayDigestState state, List<DigestLineCandidate> buffer)
        {
            List<DayDigestRecord> rebuilt = new List<DayDigestRecord>(buffer.Count);
            for (int i = 0; i < buffer.Count; i++)
            {
                rebuilt.Add(new DayDigestRecord
                {
                    tick = buffer[i].tick,
                    sourceKind = buffer[i].sourceKind,
                    line = buffer[i].line
                });
            }

            state.lines = rebuilt;
        }

        /// <summary>
        /// Discards pacing rows from earlier days. This is the day rollover: a new day restores every
        /// pawn's full allowance and forgets moments no reflection ever used. It also runs on load,
        /// where a SAME-day row survives on purpose so reloading cannot reset a pawn's pacing.
        /// </summary>
        private void PruneStaleDayDigest(int currentDay)
        {
            for (int i = dayDigestStates.Count - 1; i >= 0; i--)
            {
                PawnDayDigestState state = dayDigestStates[i];
                if (state == null || state.day < currentDay)
                {
                    dayDigestStates.RemoveAt(i);
                }
            }

            RebuildDayDigestIndex();
        }

        /// <summary>Rebuilds the transient "pawnId|day" index over the saved rows (load + prune).</summary>
        private void RebuildDayDigestIndex()
        {
            pendingDayDigest.Clear();
            if (dayDigestStates == null)
            {
                dayDigestStates = new List<PawnDayDigestState>();
                return;
            }

            for (int i = dayDigestStates.Count - 1; i >= 0; i--)
            {
                PawnDayDigestState state = dayDigestStates[i];
                if (state == null || string.IsNullOrWhiteSpace(state.pawnId))
                {
                    dayDigestStates.RemoveAt(i);
                    continue;
                }

                string key = DaySummaryKey(state.pawnId, state.day);
                if (pendingDayDigest.ContainsKey(key))
                {
                    // A hand-edited or merged save could carry the same pawn/day twice. The scan runs
                    // backwards (so removal is safe), which means the row nearest the END wins — the
                    // later-written one. Either choice is arbitrary; what matters is that exactly one
                    // row survives, or the index and the save list would disagree.
                    dayDigestStates.RemoveAt(i);
                    continue;
                }

                pendingDayDigest[key] = state;
            }
        }

        /// <summary>Saves the B6 pacing rows. Additive: an old save simply loads an empty list.</summary>
        private void ExposeDayDigestData()
        {
            Scribe_Collections.Look(ref dayDigestStates, "dayDigestStates", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return;
            }

            if (dayDigestStates == null)
            {
                dayDigestStates = new List<PawnDayDigestState>();
            }

            for (int i = 0; i < dayDigestStates.Count; i++)
            {
                dayDigestStates[i]?.Normalize();
            }

            RebuildDayDigestIndex();
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

        /// <summary>
        /// Clears all transient day-summary state (on new game / load). The B6 pacing rows are NOT
        /// cleared here: they are saved state, and wiping them on load would hand every colonist a
        /// fresh daily allowance after a reload. The rollover prune in SnapshotDayStartOpinions is
        /// what discards them, and it keeps a same-day row on purpose.
        /// </summary>
        private void ResetDaySummaryState()
        {
            pendingDayHediffs.Clear();
            dayStartOpinions.Clear();
            writtenDayReflections.Clear();
            writtenQuadrumReflections.Clear();
            opinionSnapshotDay = -1;
        }

        /// <summary>Drops every saved B6 pacing row (used when a brand-new game starts).</summary>
        private void ResetDayDigestState()
        {
            dayDigestStates.Clear();
            pendingDayDigest.Clear();
        }

        /// <summary>
        /// Rebuilds transient one-per-day guards from hot and archived saved history after load.
        /// Reflection, ambient-interaction, and ambient-thought guards intentionally stay out of the
        /// save schema; deriving them from pages prevents same-day duplicates after a reload while
        /// keeping the ambient guard sets bounded to the current day.
        /// </summary>
        private void RebuildWrittenDailyGuardsFromHistory()
        {
            writtenDayReflections.Clear();
            writtenQuadrumReflections.Clear();
            writtenAmbientInteractionNotes.Clear();
            writtenAmbientThoughtNotes.Clear();
            int currentDay = CurrentDayIndex;
            // This is a one-time load repair, not a recurring scan. Visit every retained hot row so an
            // unusually busy current day cannot push an ambient page outside the normal scan window.
            IReadOnlyList<DiaryEvent> allEvents = events.AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent ev = allEvents[i];
                if (ev == null || string.IsNullOrWhiteSpace(ev.initiatorPawnId))
                {
                    continue;
                }

                int eventDay = DayIndexForGameTick(ev.tick);
                if (string.Equals(ev.interactionDefName, DayReflectionEventData.DefNameToken, StringComparison.OrdinalIgnoreCase))
                {
                    writtenDayReflections.Add(DaySummaryKey(ev.initiatorPawnId, eventDay));
                }
                else if (string.Equals(ev.interactionDefName, DayReflectionEventData.QuadrumDefNameToken, StringComparison.OrdinalIgnoreCase))
                {
                    writtenQuadrumReflections.Add(QuadrumSummaryKey(ev.initiatorPawnId, QuadrumIndexForDay(eventDay)));
                }

                RememberAmbientDailyGuard(
                    ev.initiatorPawnId,
                    ev.interactionDefName,
                    ev.gameContext,
                    eventDay,
                    currentDay);
            }

            // Retention moves completed old pages out of the hot event store. Those archived rows are
            // still authoritative history: forgetting one permits duplicate quadrum or same-day
            // ambient pages after reload while the original timing window remains open.
            IReadOnlyList<ArchivedDiaryEntry> archivedEntries = archive.AllEntries;
            for (int i = 0; i < archivedEntries.Count; i++)
            {
                ArchivedDiaryEntry entry = archivedEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.pawnId))
                {
                    continue;
                }

                int entryDay = DayIndexForGameTick(entry.tick);
                if (string.Equals(
                    entry.interactionDefName,
                    DayReflectionEventData.QuadrumDefNameToken,
                    StringComparison.OrdinalIgnoreCase))
                {
                    writtenQuadrumReflections.Add(QuadrumSummaryKey(
                        entry.pawnId,
                        QuadrumIndexForDay(entryDay)));
                }

                RememberAmbientDailyGuard(
                    entry.pawnId,
                    entry.interactionDefName,
                    entry.decorationGameContext,
                    entryDay,
                    currentDay);
            }
        }

        /// <summary>Adds a recognized current-day ambient history row to its exact runtime guard set.</summary>
        private void RememberAmbientDailyGuard(
            string pawnId,
            string interactionDefName,
            string gameContext,
            int fallbackDay,
            int currentDay)
        {
            string interactionKey;
            string thoughtKey;
            if (!DailyEmissionGuardPolicy.TryBuildCurrentDayKeys(
                pawnId,
                interactionDefName,
                gameContext,
                fallbackDay,
                currentDay,
                out interactionKey,
                out thoughtKey))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(interactionKey))
            {
                writtenAmbientInteractionNotes.Add(interactionKey);
            }

            if (!string.IsNullOrWhiteSpace(thoughtKey))
            {
                writtenAmbientThoughtNotes.Add(thoughtKey);
            }
        }

        /// <summary>Day index a stored game tick falls in, aligned with <see cref="CurrentDayIndex"/>.</summary>
        private static int DayIndexForGameTick(int gameTick)
        {
            int offset = Find.TickManager.TicksAbs - Find.TickManager.TicksGame;
            return (gameTick + offset) / GenDate.TicksPerDay;
        }

        /// <summary>Converts an absolute world day boundary back into the saved game-tick timeline.</summary>
        private static int GameTickForDayIndex(int day)
        {
            int offset = Find.TickManager.TicksAbs - Find.TickManager.TicksGame;
            return (day * GenDate.TicksPerDay) - offset;
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
        internal struct QuadrumReflectionSignal
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

        /// <summary>One categorized archived letter ready to become a reflection candidate.</summary>
        private struct ColonyNewsSignal
        {
            public readonly string category;
            public readonly int tick;
            public readonly string evidenceLine;

            public ColonyNewsSignal(string category, int tick, string evidenceLine)
            {
                this.category = category;
                this.tick = tick;
                this.evidenceLine = evidenceLine;
            }
        }
    }
}

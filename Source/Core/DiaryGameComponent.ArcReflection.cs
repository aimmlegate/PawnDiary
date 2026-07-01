// Rare pawn life-arc reflections. This source samples existing diary pages from the current year and
// writes at most one ordinary annual chapter, plus an optional second chapter after a major event.
// It deliberately does not keep a separate fact/history store.
using System;
using System.Collections.Generic;
using System.Text;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Major progression moments call this after their normal diary page records. Cadence policy
        /// decides whether the moment warrants a rare extra arc reflection.
        /// </summary>
        private void ConsiderArcReflectionAfterMajorEvent(Pawn pawn)
        {
            if (pawn == null || !IsDiaryEligible(pawn))
            {
                return;
            }

            TryFlushArcReflectionForPawn(pawn, pawn.GetUniqueLoadID(), CurrentDayIndex, majorEventTrigger: true);
        }

        /// <summary>
        /// Attempts to emit a yearly/major arc reflection. Returns true only when an entry was created.
        /// </summary>
        private bool TryFlushArcReflectionForPawn(Pawn pawn, string pawnId, int day, bool majorEventTrigger)
        {
            if (pawn == null
                || string.IsNullOrWhiteSpace(pawnId)
                || !DiaryTuning.Current.arcReflectionEnabled)
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null)
            {
                return false;
            }

            int currentYear = CurrentRimYear();
            int dayOfYear = CurrentRimYearDay();
            int recentCap = Math.Max(0, DiaryTuning.Current.arcReflectionRecentlyUsedMemoryCap);
            PawnArcScheduleState schedule = diary.EnsureArcSchedule();
            schedule.NormalizeForYear(currentYear, recentCap);

            int nowTick = Find.TickManager.TicksGame;
            ArcReflectionScheduleDecision scheduleDecision = ArcReflectionSchedulePolicy.Evaluate(
                new ArcReflectionScheduleSnapshot
                {
                    lastArcEntryTick = schedule.lastArcEntryTick,
                    lastArcEntryYear = schedule.lastArcEntryYear,
                    arcEntriesThisYear = schedule.arcEntriesThisYear,
                    forcedArcYear = schedule.forcedArcYear
                },
                ArcScheduleTuning(),
                nowTick,
                currentYear,
                dayOfYear,
                majorEventTrigger);
            if (!scheduleDecision.allowed)
            {
                return false;
            }

            if (!majorEventTrigger && schedule.IsMemoryShortfallBackoffActive(
                nowTick,
                currentYear,
                Math.Max(0, DiaryTuning.Current.arcReflectionMemoryShortfallRetryTicks)))
            {
                return false;
            }

            List<ArcMemoryCandidate> candidates = CollectArcMemoryCandidates(diary, pawnId, currentYear, day);
            ArcMemorySelectionResult selection = ArcReflectionMemorySelector.Select(new ArcMemorySelectionRequest
            {
                candidates = candidates,
                recentlyUsedEventIds = schedule.recentlyUsedEventIds,
                currentYear = currentYear,
                maxMemories = Math.Max(1, DiaryTuning.Current.arcReflectionMaxMemories),
                minMemories = scheduleDecision.forced
                    ? Math.Max(1, DiaryTuning.Current.arcReflectionMinMemoriesForced)
                    : Math.Max(1, DiaryTuning.Current.arcReflectionMinMemoriesPreferred),
                sameDomainGroupCap = 2,
                seed = ArcSelectionSeed(pawnId, currentYear, schedule.arcEntriesThisYear, majorEventTrigger)
            });
            if (!selection.hasEnoughMemories)
            {
                if (!majorEventTrigger)
                {
                    schedule.MarkMemoryShortfall(nowTick, currentYear);
                }

                return false;
            }

            ArcReflectionEventData data = new ArcReflectionEventData
            {
                PawnId = pawnId,
                Tick = nowTick,
                DefName = ArcReflectionEventData.DefNameToken,
                ArcYear = currentYear,
                CandidateMemoryCount = selection.candidateCount,
                SelectedMemoryCount = selection.selected.Count,
                EntriesThisYear = schedule.arcEntriesThisYear,
                Forced = scheduleDecision.forced,
                AlreadyWritten = false
            };

            string label = "PawnDiary.Event.ArcReflectionLabel".Translate(currentYear).Resolve();
            string text = BuildArcReflectionText(pawn, currentYear, selection.selected);
            string instruction = "PawnDiary.Event.ArcReflectionInstruction"
                .Translate(pawn.LabelShortCap, currentYear).Resolve();
            string gameContext = ArcReflectionEventData.BuildGameContext(
                currentYear,
                scheduleDecision.forced,
                selection.selected.Count,
                selection.candidateCount,
                schedule.arcEntriesThisYear);

            if (Dispatch(new ArcReflectionSignal(data, pawn, label, text, instruction, gameContext)))
            {
                schedule.MarkArcEntry(
                    nowTick,
                    currentYear,
                    scheduleDecision.forced,
                    SelectedArcMemoryIds(selection.selected),
                    recentCap);
                return true;
            }

            return false;
        }

        private List<ArcMemoryCandidate> CollectArcMemoryCandidates(PawnDiaryRecord diary, string pawnId,
            int currentYear, int currentDay)
        {
            List<ArcMemoryCandidate> candidates = new List<ArcMemoryCandidate>();
            HashSet<string> seenEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> hotEventIds = diary?.eventIds;
            if (hotEventIds != null)
            {
                for (int i = 0; i < hotEventIds.Count; i++)
                {
                    DiaryEvent ev = events.FindEvent(hotEventIds[i]);
                    string role;
                    if (ev == null || !ev.TryGetDisplayRoleForPawn(pawnId, out role))
                    {
                        continue;
                    }

                    AddArcCandidateIfUnique(
                        candidates,
                        seenEventIds,
                        ArcCandidateFromEvent(ev, pawnId, role, currentYear, currentDay));
                }
            }

            IReadOnlyList<ArchivedDiaryEntry> archived = archive.EntriesForPawn(pawnId);
            for (int i = 0; i < archived.Count; i++)
            {
                AddArcCandidateIfUnique(
                    candidates,
                    seenEventIds,
                    ArcCandidateFromArchive(archived[i], currentYear, currentDay));
            }

            return candidates;
        }

        private static void AddArcCandidateIfUnique(List<ArcMemoryCandidate> candidates,
            HashSet<string> seenEventIds, ArcMemoryCandidate candidate)
        {
            if (candidate == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(candidate.eventId) && !seenEventIds.Add(candidate.eventId))
            {
                return;
            }

            candidates.Add(candidate);
        }

        private ArcMemoryCandidate ArcCandidateFromEvent(DiaryEvent ev, string pawnId, string role,
            int currentYear, int currentDay)
        {
            string domain = DiaryEventDomainClassifier.DomainForContext(ev.gameContext);
            string classifierKey = DiaryEventDomainClassifier.GroupClassifierKey(domain, ev.gameContext, ev.interactionDefName);
            int eventYear = YearForGameTick(ev.tick);
            return new ArcMemoryCandidate
            {
                eventId = ev.eventId,
                pawnId = pawnId,
                povRole = role,
                tick = ev.tick,
                year = eventYear,
                date = ev.date,
                defName = ev.interactionDefName,
                domain = domain,
                groupKey = classifierKey,
                label = ev.interactionLabel,
                text = ev.TextForRole(role),
                generatedText = ev.HasGeneratedTextForRole(role) ? ev.DisplayTextForRole(role) : string.Empty,
                title = ev.TitleForRole(role),
                important = ev.IsImportant(),
                reflection = IsReflectionDefName(ev.interactionDefName) || ev.IsArcReflection(),
                deathDescription = ev.HasDeathDescription(),
                sameQuadrum = QuadrumIndexForDay(DayIndexForGameTick(ev.tick)) == QuadrumIndexForDay(currentDay),
                progression = DiaryContextFields.HasMarker(ev.gameContext, "progression="),
                highStakes = IsHighStakesArcMemory(domain, ev.interactionDefName, ev.gameContext)
            };
        }

        private ArcMemoryCandidate ArcCandidateFromArchive(ArchivedDiaryEntry entry, int currentYear, int currentDay)
        {
            if (entry == null)
            {
                return null;
            }

            string context = entry.decorationGameContext ?? string.Empty;
            string domain = DiaryEventDomainClassifier.DomainForContext(context);
            string classifierKey = DiaryEventDomainClassifier.GroupClassifierKey(domain, context, entry.interactionDefName);
            int year = entry.year > 0 ? entry.year : YearForGameTick(entry.tick);
            return new ArcMemoryCandidate
            {
                eventId = entry.eventId,
                pawnId = entry.pawnId,
                povRole = entry.povRole,
                tick = entry.tick,
                year = year,
                date = entry.date,
                defName = entry.interactionDefName,
                domain = domain,
                groupKey = classifierKey,
                label = entry.interactionLabel,
                text = entry.text,
                generatedText = entry.generatedText,
                title = entry.title,
                important = entry.important,
                reflection = IsReflectionDefName(entry.interactionDefName),
                deathDescription = entry.deathDescription,
                sameQuadrum = QuadrumIndexForDay(DayIndexForGameTick(entry.tick)) == QuadrumIndexForDay(currentDay),
                progression = DiaryContextFields.HasMarker(context, "progression="),
                highStakes = IsHighStakesArcMemory(domain, entry.interactionDefName, context)
            };
        }

        private static string BuildArcReflectionText(Pawn pawn, int arcYear, List<ArcMemoryCandidate> memories)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("PawnDiary.Event.ArcReflectionHeader"
                .Translate(pawn.LabelShortCap, arcYear).Resolve());
            int cap = Math.Max(40, DiaryTuning.Current.arcReflectionMemorySnippetMaxChars);
            for (int i = 0; i < memories.Count; i++)
            {
                ArcMemoryCandidate memory = memories[i];
                string text = ArcReflectionMemorySelector.MemoryText(memory, cap);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                string date = DiaryLineCleaner.CleanLine(memory.date);
                string line = string.IsNullOrWhiteSpace(date)
                    ? text
                    : "PawnDiary.Event.ArcReflectionEvidenceLine".Translate(date, text).Resolve();
                builder.Append("\n").Append("- ").Append(line);
            }

            return builder.ToString();
        }

        private static List<string> SelectedArcMemoryIds(List<ArcMemoryCandidate> memories)
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < memories.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(memories[i]?.eventId))
                {
                    ids.Add(memories[i].eventId);
                }
            }

            return ids;
        }

        private static ArcReflectionScheduleTuning ArcScheduleTuning()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            return new ArcReflectionScheduleTuning
            {
                enabled = tuning.arcReflectionEnabled,
                maxEntriesPerYear = tuning.arcReflectionMaxEntriesPerYear,
                allowSecondMajorEntry = tuning.arcReflectionAllowSecondMajorEntry,
                secondEntryMinGapDays = tuning.arcReflectionSecondEntryMinGapDays,
                forceAfterYearDay = tuning.arcReflectionForceAfterYearDay,
                ticksPerDay = GenDate.TicksPerDay
            };
        }

        private static bool IsHighStakesArcMemory(string domain, string defName, string context)
        {
            if (string.Equals(domain, DiaryEventDomainClassifier.Romance, StringComparison.OrdinalIgnoreCase)
                || string.Equals(domain, DiaryEventDomainClassifier.Hediff, StringComparison.OrdinalIgnoreCase)
                || string.Equals(domain, DiaryEventDomainClassifier.Progression, StringComparison.OrdinalIgnoreCase)
                || DiaryContextFields.IsTrue(context, "death_description"))
            {
                return true;
            }

            return MatchesAnyToken(DiaryTuning.Current.arcReflectionHighStakesDefNameTokens, defName);
        }

        private static bool MatchesAnyToken(List<string> tokens, string value)
        {
            if (tokens == null || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (!string.IsNullOrWhiteSpace(token)
                    && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int ArcSelectionSeed(string pawnId, int year, int entriesThisYear, bool majorEvent)
        {
            unchecked
            {
                int hash = 17;
                string text = pawnId ?? string.Empty;
                for (int i = 0; i < text.Length; i++)
                {
                    hash = hash * 31 + text[i];
                }

                hash = hash * 31 + year;
                hash = hash * 31 + entriesThisYear;
                hash = hash * 31 + (majorEvent ? 1 : 0);
                return hash == int.MinValue ? 1 : Math.Abs(hash);
            }
        }

        private static int CurrentRimYear()
        {
            return GenDate.Year(Find.TickManager.TicksAbs, 0f);
        }

        private static int CurrentRimYearDay()
        {
            int day = CurrentDayIndex % GenDate.DaysPerYear;
            return day < 0 ? day + GenDate.DaysPerYear : day;
        }

        private static int YearForGameTick(int gameTick)
        {
            int absTick = gameTick + Find.TickManager.TicksAbs - Find.TickManager.TicksGame;
            return GenDate.Year(absTick, 0f);
        }
    }
}

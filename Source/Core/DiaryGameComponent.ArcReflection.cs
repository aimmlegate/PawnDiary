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
        /// Major progression moments call this after their normal diary page records. N4 stores one
        /// bounded request for the pawn's next natural rest arbitration instead of creating an immediate
        /// second page beside the canonical source event.
        /// </summary>
        private void ConsiderArcReflectionAfterMajorEvent(Pawn pawn, string avoidRelatedEventId = null)
        {
            if (pawn == null
                || !IsDiaryEligible(pawn)
                || !pawn.Spawned
                || pawn.Map == null
                || !DiaryTuning.Current.arcReflectionEnabled
                || !IsReflectionGroupEnabled(ArcReflectionEventData.DefNameToken))
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null)
            {
                return;
            }

            PawnReflectionState reflectionState = diary.EnsureReflectionState();
            if (reflectionState.baselineOnNextOpportunity)
            {
                // A real post-upgrade event establishes the N4 boundary immediately, so its new pending
                // request is preserved without allowing older annual/day/quadrum debt to catch up.
                BaselineReflectionState(
                    diary,
                    pawnId,
                    CurrentDayIndex,
                    Find.TickManager.TicksGame,
                    reflectionState,
                    DaySummaryOwnsFiller(DiaryNarrativeContinuityPolicy.Snapshot()));
            }

            reflectionState.QueueMajorArc(Find.TickManager.TicksGame, avoidRelatedEventId);
        }

        /// <summary>
        /// Queues N4 only when an already-created terminal page still carries the exact source-owned
        /// evidence and reference promised by its callback. This is the impure bridge around the pure
        /// qualification policy; it never creates a second page at the terminal boundary.
        /// </summary>
        internal void ConsiderArcReflectionAfterTerminalEvent(
            Pawn pawn,
            DiaryEvent diaryEvent,
            string povRole,
            TerminalReflectionContract contract)
        {
            if (pawn == null || diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            TerminalReflectionDecision decision = TerminalReflectionPolicy.Evaluate(
                new TerminalReflectionRequest
                {
                    canonicalEventId = diaryEvent.eventId,
                    canonicalEventTick = diaryEvent.tick,
                    povPawnId = pawnId,
                    povRole = povRole,
                    contract = contract,
                    evidence = diaryEvent.NarrativeEvidenceForRole(povRole),
                    references = diaryEvent.NarrativeReferencesForRole(povRole)
                });
            if (decision.queueMajorArc)
            {
                ConsiderArcReflectionAfterMajorEvent(pawn, decision.avoidRelatedEventId);
            }
        }

        /// <summary>
        /// Collects one detached annual/major opportunity and retains the existing selected memories only
        /// in this impure runtime candidate. No cadence/pending state is consumed during collection.
        /// </summary>
        private ReflectionRuntimeCandidate PrepareArcReflectionCandidate(
            Pawn pawn,
            PawnDiaryRecord diary,
            string pawnId,
            int day,
            bool majorEventTrigger,
            string avoidRelatedEventId,
            PawnReflectionState pendingOwner,
            bool collectEvidence)
        {
            int currentYear = CurrentRimYear();
            int dayOfYear = CurrentRimYearDay();
            int recentCap = Math.Max(0, DiaryTuning.Current.arcReflectionRecentlyUsedMemoryCap);
            PawnArcScheduleState schedule = diary.EnsureArcSchedule();
            // Applying a lowered XML cap and rolling the saved yearly counter forward are maintenance,
            // not cadence consumption, so they remain safe before the coordinator selects anything.
            schedule.NormalizeForYear(currentYear, recentCap);

            int nowTick = Find.TickManager.TicksGame;
            ArcReflectionScheduleTuning cadence = ArcScheduleTuning();
            int terminalRetryMaxTicks =
                Math.Max(0, DiaryTuning.Current.arcReflectionTerminalRetryMaxTicks);
            bool retryTerminalRequest = TerminalReflectionRetryPolicy.CanRetry(
                majorEventTrigger,
                avoidRelatedEventId,
                pendingOwner?.pendingMajorArcRequestedTick ?? -1,
                nowTick,
                terminalRetryMaxTicks);
            bool terminalRequestExpired = TerminalReflectionRetryPolicy.IsExpired(
                avoidRelatedEventId,
                pendingOwner?.pendingMajorArcRequestedTick ?? -1,
                nowTick,
                terminalRetryMaxTicks);
            // Evaluate the underlying cadence even while the source/group is disabled so the coordinator
            // can issue an explicit debt-bounding instruction without scanning any memory archive.
            cadence.enabled = true;
            ArcReflectionScheduleDecision scheduleDecision = ArcReflectionSchedulePolicy.Evaluate(
                new ArcReflectionScheduleSnapshot
                {
                    lastArcEntryTick = schedule.lastArcEntryTick,
                    lastArcEntryYear = schedule.lastArcEntryYear,
                    arcEntriesThisYear = schedule.arcEntriesThisYear,
                    forcedArcYear = schedule.forcedArcYear
                },
                cadence,
                nowTick,
                currentYear,
                dayOfYear,
                majorEventTrigger);

            bool groupEnabled = DiaryTuning.Current.arcReflectionEnabled
                && IsReflectionGroupEnabled(ArcReflectionEventData.DefNameToken);
            ReflectionRuntimeCandidate runtime = new ReflectionRuntimeCandidate
            {
                opportunity = new ReflectionOpportunity
                {
                    kind = NarrativeReflectionKindTokens.MajorArc,
                    pawnId = pawnId,
                    nowTick = nowTick,
                    candidateMemoryCount = 1,
                    importance = majorEventTrigger
                        ? NarrativeSalienceTokens.Major
                        : NarrativeSalienceTokens.Meaningful,
                    due = scheduleDecision.allowed,
                    alreadyWritten = false,
                    cooldownSatisfied = true,
                    groupEnabled = groupEnabled
                }
            };
            if (majorEventTrigger && terminalRequestExpired)
            {
                runtime.opportunity.due = false;
                runtime.settleIneligible = pendingOwner == null
                    ? (Action)null
                    : pendingOwner.ClearPendingMajorArc;
                return runtime;
            }

            if ((!majorEventTrigger || retryTerminalRequest)
                && schedule.IsMemoryShortfallBackoffActive(
                nowTick,
                currentYear,
                Math.Max(0, DiaryTuning.Current.arcReflectionMemoryShortfallRetryTicks)))
            {
                runtime.opportunity.due = false;
            }

            if (!runtime.opportunity.due)
            {
                if (majorEventTrigger && pendingOwner != null)
                {
                    // Terminal-owned debt survives cadence and shortfall checks only inside its XML-backed
                    // retry window. Ordinary major events keep the original one-rest bound.
                    runtime.settleIneligible = retryTerminalRequest
                        ? (Action)(() => schedule.MarkMemoryShortfall(nowTick, currentYear))
                        : pendingOwner.ClearPendingMajorArc;
                }

                return runtime;
            }

            runtime.advanceDisabledDebt = () =>
            {
                if (majorEventTrigger)
                {
                    pendingOwner?.ClearPendingMajorArc();
                }
                else if (scheduleDecision.forced)
                {
                    schedule.forcedArcYear = currentYear;
                }
            };
            if (!groupEnabled)
            {
                return runtime;
            }

            if (!collectEvidence)
            {
                return runtime;
            }

            List<ArcMemoryCandidate> candidates = CollectArcMemoryCandidates(diary, pawnId, currentYear, day);
            // A caller may ask this reflection to avoid recapping one just-created related event (for
            // example the terminal void ending). That id is excluded only for this selection; the
            // durable recently-used history is unchanged.
            List<string> excludedEventIds = schedule.recentlyUsedEventIds;
            if (!string.IsNullOrWhiteSpace(avoidRelatedEventId))
            {
                excludedEventIds = new List<string>(schedule.recentlyUsedEventIds ?? new List<string>())
                {
                    avoidRelatedEventId
                };
            }
            ArcMemorySelectionResult selection = ArcReflectionMemorySelector.Select(new ArcMemorySelectionRequest
            {
                candidates = candidates,
                recentlyUsedEventIds = excludedEventIds,
                currentYear = currentYear,
                maxMemories = Math.Max(1, DiaryTuning.Current.arcReflectionMaxMemories),
                minMemories = scheduleDecision.forced
                    ? Math.Max(1, DiaryTuning.Current.arcReflectionMinMemoriesForced)
                    : Math.Max(1, DiaryTuning.Current.arcReflectionMinMemoriesPreferred),
                sameDomainGroupCap = 2,
                seed = ArcSelectionSeed(
                    pawnId, currentYear, scheduleDecision.normalizedEntriesThisYear, majorEventTrigger)
            });
            if (!selection.hasEnoughMemories)
            {
                runtime.opportunity.due = false;
                if (majorEventTrigger && pendingOwner != null)
                {
                    runtime.settleIneligible = retryTerminalRequest
                        ? (Action)(() => schedule.MarkMemoryShortfall(nowTick, currentYear))
                        : pendingOwner.ClearPendingMajorArc;
                }
                else if (!majorEventTrigger)
                {
                    runtime.settleIneligible = () => schedule.MarkMemoryShortfall(nowTick, currentYear);
                }
                return runtime;
            }

            List<string> selectedIds = SelectedArcMemoryIds(selection.selected);
            runtime.opportunity.candidateMemoryCount = selection.candidateCount;
            runtime.opportunity.sourceEventIds = selectedIds;
            runtime.dispatch = () => DispatchPreparedArcReflection(
                pawn,
                pawnId,
                nowTick,
                currentYear,
                scheduleDecision,
                selection);
            runtime.consumeAfterDispatch = () =>
            {
                schedule.MarkArcEntry(
                    nowTick,
                    currentYear,
                    scheduleDecision.forced,
                    selectedIds,
                    recentCap);
                if (majorEventTrigger)
                {
                    pendingOwner?.ClearPendingMajorArc();
                }
            };
            return runtime;
        }

        private bool DispatchPreparedArcReflection(
            Pawn pawn,
            string pawnId,
            int nowTick,
            int currentYear,
            ArcReflectionScheduleDecision scheduleDecision,
            ArcMemorySelectionResult selection)
        {
            ArcReflectionEventData data = new ArcReflectionEventData
            {
                PawnId = pawnId,
                Tick = nowTick,
                DefName = ArcReflectionEventData.DefNameToken,
                ArcYear = currentYear,
                CandidateMemoryCount = selection.candidateCount,
                SelectedMemoryCount = selection.selected.Count,
                EntriesThisYear = scheduleDecision.normalizedEntriesThisYear,
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
                scheduleDecision.normalizedEntriesThisYear);

            return Dispatch(new ArcReflectionSignal(data, pawn, label, text, instruction, gameContext));
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
            string groupKey = ArcGroupKeyForSavedEvent(domain, ev.gameContext, ev.interactionDefName);
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
                groupKey = groupKey,
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
            string groupKey = ArcGroupKeyForSavedEvent(domain, context, entry.interactionDefName);
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
                groupKey = groupKey,
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

        /// <summary>
        /// Recovers the diversity-bucket key for a saved memory. Ordinary Quest memories preserve their
        /// historical signal bucket, while an explicit reviewed root uses its exact group so Royal
        /// Ascent is not capped together with every generic completed/failed quest.
        /// </summary>
        private static string ArcGroupKeyForSavedEvent(string domain, string context, string defName)
        {
            string classifierKey = DiaryEventDomainClassifier.GroupClassifierKey(domain, context, defName);
            if (!string.Equals(domain, DiaryEventDomainClassifier.Quest, StringComparison.OrdinalIgnoreCase))
            {
                return classifierKey;
            }

            string questRoot = DiaryEventDomainClassifier.QuestRootClassifierKey(domain, context, defName);
            DiaryInteractionGroupDef exact = InteractionGroups.ClassifyQuestRoot(questRoot);
            return exact != null ? exact.defName : classifierKey;
        }

        private static string BuildArcReflectionText(Pawn pawn, int arcYear, List<ArcMemoryCandidate> memories)
        {
            string header = "PawnDiary.Event.ArcReflectionHeader"
                .Translate(pawn.LabelShortCap, arcYear).Resolve();
            return BuildReflectionEvidenceText(header, memories);
        }

        /// <summary>
        /// Formats already-selected factual memory lines below a localized header. Annual and linked
        /// reflections share this evidence shape, while each caller owns its truthful time framing.
        /// </summary>
        private static string BuildReflectionEvidenceText(
            string header,
            List<ArcMemoryCandidate> memories)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(header ?? string.Empty);
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

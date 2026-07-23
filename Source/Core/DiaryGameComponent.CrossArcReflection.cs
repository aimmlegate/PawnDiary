// Impure N4.2 adapter for linked cross-arc reflections. It reads hot events and the compact per-pawn
// archive, projects only plain saved references/text into the assembly-free selector, and reuses the
// existing ArcReflection signal after the shared coordinator chooses this detached opportunity.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>Builds one detached cross-arc opportunity without consuming any saved state.</summary>
        private ReflectionRuntimeCandidate PrepareCrossArcReflectionCandidate(
            Pawn pawn,
            PawnDiaryRecord diary,
            string pawnId,
            PawnReflectionState state,
            NarrativePolicySnapshot policy,
            bool collectEvidence)
        {
            int nowTick = Find.TickManager.TicksGame;
            bool groupEnabled = DiaryTuning.Current.arcReflectionEnabled
                && IsReflectionGroupEnabled(ArcReflectionEventData.DefNameToken);
            ReflectionRuntimeCandidate runtime = new ReflectionRuntimeCandidate
            {
                opportunity = new ReflectionOpportunity
                {
                    kind = NarrativeReflectionKindTokens.CrossArc,
                    pawnId = pawnId,
                    nowTick = nowTick,
                    due = false,
                    groupEnabled = groupEnabled,
                    cooldownSatisfied = true
                }
            };
            if (!groupEnabled || !collectEvidence || policy == null || !policy.enabled)
            {
                return runtime;
            }

            CrossArcMemorySelection selection = CrossArcReflectionMemorySelector.Select(
                new CrossArcMemorySelectionRequest
                {
                    pawnId = pawnId,
                    currentTick = nowTick,
                    eligibleAfterTick = state?.lastCrossArcTick ?? -1,
                    candidateScanCap = policy.reflectionCandidateScanCap,
                    memoryCap = policy.reflectionMemoryCap,
                    minimumLinkedMemories = policy.reflectionMinimumLinkedMemories,
                    minimumDistinctPhases = policy.reflectionMinimumDistinctPhases,
                    maximumSpanTicks = policy.reflectionMaximumSpanTicks,
                    requireChangeOrConsequence = policy.reflectionRequireChangeOrConsequence,
                    changeOrConsequenceFacets =
                        new List<string>(policy.reflectionChangeOrConsequenceFacets
                            ?? new List<string>()),
                    candidates = CollectCrossArcMemoryCandidates(diary, pawnId)
                });

            runtime.opportunity.candidateMemoryCount = selection.candidateCount;
            runtime.opportunity.linkedMemoryCount = selection.linkedMemoryCount;
            runtime.opportunity.memorySpanTicks = selection.memorySpanTicks;
            runtime.opportunity.sourceEventIds = new List<string>(selection.sourceEventIds);
            runtime.opportunity.arcKeys = new List<string>(selection.arcKeys);
            runtime.opportunity.hasCoherentLink = selection.hasCoherentLink;
            runtime.opportunity.hasPhaseChange = selection.hasPhaseChange;
            runtime.opportunity.hasChangeOrConsequence = selection.hasChangeOrConsequence;
            runtime.opportunity.importance = CrossArcImportance(selection.selected);
            runtime.opportunity.due = selection.qualified;
            if (!selection.qualified)
            {
                return runtime;
            }

            runtime.dispatch = () => DispatchPreparedCrossArcReflection(pawn, pawnId, nowTick, selection);
            // The shared adapter records lastCrossArcTick only after Dispatch succeeds. There is no
            // separate pending row to mutate here; the source pages remain factual history.
            runtime.consumeAfterDispatch = () => { };
            return runtime;
        }

        private List<CrossArcMemoryCandidate> CollectCrossArcMemoryCandidates(
            PawnDiaryRecord diary,
            string pawnId)
        {
            List<CrossArcMemoryCandidate> result = new List<CrossArcMemoryCandidate>();
            List<string> hotIds = diary?.eventIds;
            if (hotIds != null)
            {
                for (int i = 0; i < hotIds.Count; i++)
                {
                    DiaryEvent diaryEvent = events.FindEvent(hotIds[i]);
                    string role;
                    if (diaryEvent == null || !diaryEvent.TryGetDisplayRoleForPawn(pawnId, out role))
                    {
                        continue;
                    }

                    CrossArcMemoryCandidate candidate = CrossArcCandidateFromEvent(
                        diaryEvent, pawnId, role);
                    if (candidate != null) result.Add(candidate);
                }
            }

            IReadOnlyList<ArchivedDiaryEntry> archived = archive.EntriesForPawn(pawnId);
            for (int i = 0; i < archived.Count; i++)
            {
                CrossArcMemoryCandidate candidate = CrossArcCandidateFromArchive(archived[i]);
                if (candidate != null) result.Add(candidate);
            }

            return result;
        }

        /// <summary>Projects one hot POV page, including its own exact source evidence, for RimTests.</summary>
        internal static CrossArcMemoryCandidate CrossArcCandidateFromEvent(
            DiaryEvent diaryEvent,
            string pawnId,
            string role)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(pawnId)
                || string.IsNullOrWhiteSpace(role))
            {
                return null;
            }

            List<NarrativeReference> references = new List<NarrativeReference>();
            List<NarrativeEvidence> evidence = diaryEvent.NarrativeEvidenceForRole(role);
            for (int i = 0; i < evidence.Count; i++)
            {
                references.Add(NarrativeReferencePolicy.FromEvidence(evidence[i]));
            }
            references.AddRange(diaryEvent.NarrativeReferencesForRole(role));

            return new CrossArcMemoryCandidate
            {
                eventId = diaryEvent.eventId,
                pawnId = pawnId,
                tick = diaryEvent.tick,
                date = diaryEvent.date,
                text = diaryEvent.TextForRole(role),
                generatedText = diaryEvent.HasGeneratedTextForRole(role)
                    ? diaryEvent.DisplayTextForRole(role)
                    : string.Empty,
                title = diaryEvent.TitleForRole(role),
                label = diaryEvent.interactionLabel,
                salience = diaryEvent.IsImportant()
                    ? NarrativeSalienceTokens.Major
                    : NarrativeSalienceTokens.Minor,
                reflection = IsReflectionDefName(diaryEvent.interactionDefName)
                    || diaryEvent.IsArcReflection()
                    || diaryEvent.IsBeliefReflection(),
                recap = IsReflectionDefName(diaryEvent.interactionDefName),
                references = NarrativePersistencePolicy.NormalizeReferences(references)
            };
        }

        /// <summary>Projects one compact archive row through its persisted exact references.</summary>
        internal static CrossArcMemoryCandidate CrossArcCandidateFromArchive(ArchivedDiaryEntry entry)
        {
            if (entry == null) return null;
            return new CrossArcMemoryCandidate
            {
                eventId = entry.eventId,
                pawnId = entry.pawnId,
                tick = entry.tick,
                date = entry.date,
                text = entry.text,
                generatedText = entry.generatedText,
                title = entry.title,
                label = entry.interactionLabel,
                salience = entry.important
                    ? NarrativeSalienceTokens.Major
                    : NarrativeSalienceTokens.Minor,
                reflection = IsReflectionDefName(entry.interactionDefName)
                    || DiaryContextFields.IsTrue(entry.decorationGameContext, "belief_reflection"),
                recap = IsReflectionDefName(entry.interactionDefName),
                references = NarrativeStatePersistence.ToReferences(entry.narrativeReferences)
            };
        }

        private bool DispatchPreparedCrossArcReflection(
            Pawn pawn,
            string pawnId,
            int nowTick,
            CrossArcMemorySelection selection)
        {
            List<ArcMemoryCandidate> memories = new List<ArcMemoryCandidate>();
            for (int i = 0; i < selection.selected.Count; i++)
            {
                CrossArcMemoryCandidate source = selection.selected[i];
                memories.Add(new ArcMemoryCandidate
                {
                    eventId = source.eventId,
                    pawnId = source.pawnId,
                    povRole = DiaryEvent.InitiatorRole,
                    tick = source.tick,
                    date = source.date,
                    label = source.label,
                    text = source.text,
                    generatedText = source.generatedText,
                    title = source.title,
                    important = source.salience == NarrativeSalienceTokens.Major
                        || source.salience == NarrativeSalienceTokens.Terminal
                });
            }

            ArcReflectionEventData data = new ArcReflectionEventData
            {
                PawnId = pawnId,
                Tick = nowTick,
                DefName = ArcReflectionEventData.DefNameToken,
                ArcYear = CurrentRimYear(),
                CandidateMemoryCount = selection.candidateCount,
                SelectedMemoryCount = memories.Count,
                EntriesThisYear = 0,
                Forced = false,
                AlreadyWritten = false
            };
            string label = "PawnDiary.Event.CrossArcReflectionLabel".Translate().Resolve();
            string header = "PawnDiary.Event.CrossArcReflectionHeader"
                .Translate(pawn.LabelShortCap).Resolve();
            string text = BuildReflectionEvidenceText(header, memories);
            string instruction = "PawnDiary.Event.CrossArcReflectionInstruction"
                .Translate(pawn.LabelShortCap).Resolve();
            // Deliberately omit arc_year/forced/entries_this_year: linked memories may cross a year
            // boundary, and the reused ArcReflection template hides absent optional fields.
            string gameContext = "arc_reflection=true; cross_arc_reflection=true"
                + "; selected_memories=" + memories.Count
                + "; candidate_memories=" + selection.candidateCount
                + "; linked_memories=" + selection.linkedMemoryCount
                + "; distinct_phases=" + selection.distinctPhaseCount;
            return Dispatch(new ArcReflectionSignal(data, pawn, label, text, instruction, gameContext));
        }

        private static string CrossArcImportance(List<CrossArcMemoryCandidate> selected)
        {
            string best = NarrativeSalienceTokens.Meaningful;
            for (int i = 0; i < selected.Count; i++)
            {
                string salience = selected[i]?.salience;
                if (salience == NarrativeSalienceTokens.Terminal) return salience;
                if (salience == NarrativeSalienceTokens.Major) best = salience;
            }
            return best;
        }
    }
}

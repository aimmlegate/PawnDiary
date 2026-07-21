// The DiaryEvent factory: the two constructors every Record* hook funnels through.
// AddPairwiseEvent/AddSoloEvent stamp a new DiaryEvent with an id, date, the fallback game text, and
// the per-POV context summaries used by prompt policies (pawn, surroundings, continuity, previous
// page ending, weapon),
// register it in the event store, and cross-reference the involved pawns' records. The per-event text and
// context builders that feed these (tale helpers, mental-break text, …) live in each event's own
// file — DiaryGameComponent.Tales.cs, .MentalStates.cs, etc.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Creates a DiaryEvent involving two pawns (initiator + recipient), enriches it with context
        /// summaries, registers it, and cross-references both pawns' diary records.
        /// </summary>
        internal DiaryEvent AddPairwiseEvent(Pawn initiator, Pawn recipient, string defName, string label,
            string initiatorText, string recipientText, string instruction, string gameContext)
        {
            return AddPairwiseEventCore(
                initiator,
                recipient,
                defName,
                label,
                initiatorText,
                recipientText,
                instruction,
                gameContext,
                null,
                -1,
                null,
                null);
        }

        /// <summary>Creates a pair page and decorates each eligible POV from one plain evidence row.</summary>
        internal DiaryEvent AddPairwiseEvent(Pawn initiator, Pawn recipient, string defName, string label,
            string initiatorText, string recipientText, string instruction, string gameContext,
            BeliefEventEvidence beliefEvidence)
        {
            return AddPairwiseEventCore(initiator, recipient, defName, label, initiatorText,
                recipientText, instruction, gameContext, null, -1, beliefEvidence, null);
        }

        /// <summary>
        /// Creates a pair page at a historical birth tick from context frozen at that birth boundary.
        /// </summary>
        internal DiaryEvent AddPairwiseEvent(Pawn initiator, Pawn recipient, string defName, string label,
            string initiatorText, string recipientText, string instruction, string gameContext,
            BirthEventContextSnapshot capturedContext, int historicalTick)
        {
            return AddPairwiseEventCore(
                initiator,
                recipient,
                defName,
                label,
                initiatorText,
                recipientText,
                instruction,
                gameContext,
                capturedContext,
                historicalTick,
                null,
                null);
        }

        /// <summary>Creates a pair page from a staged signal at its original captured tick.</summary>
        internal DiaryEvent AddPairwiseEvent(Pawn initiator, Pawn recipient, string defName, string label,
            string initiatorText, string recipientText, string instruction, string gameContext,
            int historicalTick)
        {
            return AddPairwiseEventCore(
                initiator,
                recipient,
                defName,
                label,
                initiatorText,
                recipientText,
                instruction,
                gameContext,
                null,
                historicalTick,
                null,
                null);
        }

        /// <summary>Creates a staged pair page with plain event-time belief evidence.</summary>
        internal DiaryEvent AddPairwiseEvent(Pawn initiator, Pawn recipient, string defName, string label,
            string initiatorText, string recipientText, string instruction, string gameContext,
            int historicalTick, BeliefEventEvidence beliefEvidence)
        {
            return AddPairwiseEventCore(initiator, recipient, defName, label, initiatorText,
                recipientText, instruction, gameContext, null, historicalTick, beliefEvidence, null);
        }

        private DiaryEvent AddPairwiseEventCore(
            Pawn initiator,
            Pawn recipient,
            string defName,
            string label,
            string initiatorText,
            string recipientText,
            string instruction,
            string gameContext,
            BirthEventContextSnapshot capturedContext,
            int historicalTick,
            BeliefEventEvidence beliefEvidence,
            BeliefContextBuildResult preparedInitiatorBelief)
        {
            IReadOnlyList<DiaryEvent> activeEvents = ActiveScanEvents();
            string initiatorId = initiator.GetUniqueLoadID();
            string recipientId = recipient.GetUniqueLoadID();
            BirthWriterContextSnapshot initiatorCapture = FindBirthWriterContext(capturedContext, initiatorId);
            BirthWriterContextSnapshot recipientCapture = FindBirthWriterContext(capturedContext, recipientId);
            string initiatorName = CapturedOrLiveName(initiatorCapture, initiator);
            string recipientName = CapturedOrLiveName(recipientCapture, recipient);
            string neutralText = string.Equals(initiatorText, recipientText, StringComparison.OrdinalIgnoreCase)
                ? initiatorText
                : initiatorName + ": " + initiatorText + " / " + recipientName + ": " + recipientText;

            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = Guid.NewGuid().ToString("N"),
                tick = ResolveEventTick(historicalTick),
                date = ResolveEventDate(capturedContext, historicalTick),
                interactionDefName = defName,
                interactionLabel = label,
                initiatorPawnId = initiatorId,
                recipientPawnId = recipientId,
                initiatorName = initiatorName,
                recipientName = recipientName,
                initiatorText = initiatorText,
                recipientText = recipientText,
                neutralText = neutralText,
                gameContext = gameContext,
                instruction = instruction,
                colorCue = DiaryEvent.ResolveColorCue(defName, gameContext),
                initiatorPawnSummary = CapturedOrFallback(initiatorCapture?.pawnSummary,
                    () => DiaryContextBuilder.BuildPawnSummary(initiator)),
                recipientPawnSummary = CapturedOrFallback(recipientCapture?.pawnSummary,
                    () => DiaryContextBuilder.BuildPawnSummary(recipient)),
                initiatorSurroundings = CapturedOrFallback(initiatorCapture?.surroundings,
                    () => DiaryContextBuilder.BuildSurroundingsSummary(initiator)),
                recipientSurroundings = CapturedOrFallback(recipientCapture?.surroundings,
                    () => DiaryContextBuilder.BuildSurroundingsSummary(recipient)),
                initiatorContinuity = CapturedOrFallback(initiatorCapture?.pairContinuity,
                    () => DiaryContextBuilder.BuildContinuitySummary(initiator, recipient, activeEvents)),
                recipientContinuity = CapturedOrFallback(recipientCapture?.pairContinuity,
                    () => DiaryContextBuilder.BuildContinuitySummary(recipient, initiator, activeEvents)),
                initiatorLastOpener = CapturedOrFallback(initiatorCapture?.lastOpener,
                    () => DiaryContextBuilder.LatestDiaryOpener(initiatorId, activeEvents), true),
                recipientLastOpener = CapturedOrFallback(recipientCapture?.lastOpener,
                    () => DiaryContextBuilder.LatestDiaryOpener(recipientId, activeEvents), true),
                initiatorPreviousEntryEnding = CapturedOrFallback(initiatorCapture?.previousEntryEnding,
                    () => DiaryContextBuilder.LatestDiaryEnding(initiatorId, activeEvents), true),
                recipientPreviousEntryEnding = CapturedOrFallback(recipientCapture?.previousEntryEnding,
                    () => DiaryContextBuilder.LatestDiaryEnding(recipientId, activeEvents), true),
                initiatorWeapon = CapturedOrFallback(initiatorCapture?.weapon,
                    () => DiaryContextBuilder.EquippedWeapon(initiator), true),
                recipientWeapon = CapturedOrFallback(recipientCapture?.weapon,
                    () => DiaryContextBuilder.EquippedWeapon(recipient), true),
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            ApplyCapturedOrLiveGenerationEligibility(
                diaryEvent, DiaryEvent.InitiatorRole, initiator, initiatorCapture);
            ApplyCapturedOrLiveGenerationEligibility(
                diaryEvent, DiaryEvent.RecipientRole, recipient, recipientCapture);
            ApplyBeliefContext(diaryEvent, DiaryEvent.InitiatorRole, initiator,
                beliefEvidence, preparedInitiatorBelief);
            ApplyBeliefContext(diaryEvent, DiaryEvent.RecipientRole, recipient,
                beliefEvidence, null);
            // Snapshot display-only pawn facts (staggered handwriting, text-decoration hediff/trait
            // names) from the live pawns here, then store plain values on the model. After this the
            // DiaryEvent never touches Pawn state. See PawnFactCapture / AGENTS.md ("barrier").
            diaryEvent.SetStaggeredIntensity(DiaryEvent.InitiatorRole,
                initiatorCapture?.staggeredIntensity ?? PawnFactCapture.StaggeredIntensity(initiator));
            diaryEvent.SetStaggeredIntensity(DiaryEvent.RecipientRole,
                recipientCapture?.staggeredIntensity ?? PawnFactCapture.StaggeredIntensity(recipient));
            diaryEvent.SetTextDecorationFacts(DiaryEvent.InitiatorRole,
                initiatorCapture?.textDecorationFacts ?? PawnFactCapture.TextDecorationFacts(initiator));
            diaryEvent.SetTextDecorationFacts(DiaryEvent.RecipientRole,
                recipientCapture?.textDecorationFacts ?? PawnFactCapture.TextDecorationFacts(recipient));
            events.Register(diaryEvent);
            AddEventRef(initiator, diaryEvent.eventId, historicalTick >= 0);
            AddEventRef(recipient, diaryEvent.eventId, historicalTick >= 0);
            ApplyDiaryEventLimits();
            // Associative memory: recall BEFORE deposit so this event cannot recall its own fragment.
            ApplyMemoryContextForEvent(diaryEvent);
            DepositMemoryFragments(diaryEvent);
            if (diaryEvent.IsSkipped(DiaryEvent.InitiatorRole))
            {
                NotifyEntryStatusChanged(diaryEvent, DiaryEvent.InitiatorRole);
            }

            if (diaryEvent.IsSkipped(DiaryEvent.RecipientRole))
            {
                NotifyEntryStatusChanged(diaryEvent, DiaryEvent.RecipientRole);
            }

            return diaryEvent;
        }

        /// <summary>
        /// Creates a solo DiaryEvent (single-POV, e.g. a mental break) with no recipient role.
        /// </summary>
        internal DiaryEvent AddSoloEvent(Pawn pawn, Pawn otherPawn, string defName, string label,
            string text, string instruction, string gameContext)
        {
            return AddSoloEventCore(
                pawn, otherPawn, defName, label, text, instruction, gameContext, null, -1, null, null);
        }

        /// <summary>Creates a solo page with one plain event-relative belief evidence row.</summary>
        internal DiaryEvent AddSoloEvent(Pawn pawn, Pawn otherPawn, string defName, string label,
            string text, string instruction, string gameContext, BeliefEventEvidence beliefEvidence)
        {
            return AddSoloEventCore(pawn, otherPawn, defName, label, text, instruction, gameContext,
                null, -1, beliefEvidence, null);
        }

        /// <summary>
        /// Creates a solo page with an already-evaluated result. Body-mod capture uses this seam so
        /// the same resolver pass supplies both its legacy attitude token and its saved prompt source.
        /// </summary>
        internal DiaryEvent AddSoloEvent(Pawn pawn, Pawn otherPawn, string defName, string label,
            string text, string instruction, string gameContext, BeliefEventEvidence beliefEvidence,
            BeliefContextBuildResult preparedBelief)
        {
            return AddSoloEventCore(pawn, otherPawn, defName, label, text, instruction, gameContext,
                null, -1, beliefEvidence, preparedBelief);
        }

        /// <summary>
        /// Creates a solo page at a historical birth tick from context frozen at that birth boundary.
        /// </summary>
        internal DiaryEvent AddSoloEvent(Pawn pawn, Pawn otherPawn, string defName, string label,
            string text, string instruction, string gameContext,
            BirthEventContextSnapshot capturedContext, int historicalTick)
        {
            return AddSoloEventCore(
                pawn,
                otherPawn,
                defName,
                label,
                text,
                instruction,
                gameContext,
                capturedContext,
                historicalTick,
                null,
                null);
        }

        /// <summary>Creates a solo page from a staged signal at its original captured tick.</summary>
        internal DiaryEvent AddSoloEvent(Pawn pawn, Pawn otherPawn, string defName, string label,
            string text, string instruction, string gameContext, int historicalTick)
        {
            return AddSoloEventCore(
                pawn,
                otherPawn,
                defName,
                label,
                text,
                instruction,
                gameContext,
                null,
                historicalTick,
                null,
                null);
        }

        /// <summary>Creates a staged solo page with plain event-time belief evidence.</summary>
        internal DiaryEvent AddSoloEvent(Pawn pawn, Pawn otherPawn, string defName, string label,
            string text, string instruction, string gameContext, int historicalTick,
            BeliefEventEvidence beliefEvidence)
        {
            return AddSoloEventCore(pawn, otherPawn, defName, label, text, instruction, gameContext,
                null, historicalTick, beliefEvidence, null);
        }

        private DiaryEvent AddSoloEventCore(
            Pawn pawn,
            Pawn otherPawn,
            string defName,
            string label,
            string text,
            string instruction,
            string gameContext,
            BirthEventContextSnapshot capturedContext,
            int historicalTick,
            BeliefEventEvidence beliefEvidence,
            BeliefContextBuildResult preparedBelief)
        {
            IReadOnlyList<DiaryEvent> activeEvents = ActiveScanEvents();
            string pawnId = pawn.GetUniqueLoadID();
            BirthWriterContextSnapshot pawnCapture = FindBirthWriterContext(capturedContext, pawnId);
            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = Guid.NewGuid().ToString("N"),
                tick = ResolveEventTick(historicalTick),
                date = ResolveEventDate(capturedContext, historicalTick),
                interactionDefName = defName,
                interactionLabel = label,
                initiatorPawnId = pawnId,
                recipientPawnId = string.Empty,
                initiatorName = CapturedOrLiveName(pawnCapture, pawn),
                recipientName = string.Empty,
                initiatorText = text,
                recipientText = string.Empty,
                neutralText = text,
                gameContext = gameContext,
                instruction = instruction,
                colorCue = DiaryEvent.ResolveColorCue(defName, gameContext),
                initiatorPawnSummary = CapturedOrFallback(pawnCapture?.pawnSummary,
                    () => DiaryContextBuilder.BuildPawnSummary(pawn)),
                recipientPawnSummary = "n/a",
                initiatorSurroundings = CapturedOrFallback(pawnCapture?.surroundings,
                    () => DiaryContextBuilder.BuildSurroundingsSummary(pawn)),
                recipientSurroundings = "n/a",
                initiatorContinuity = CapturedOrFallback(pawnCapture?.continuity,
                    () => DiaryContextBuilder.BuildContinuitySummary(pawn, otherPawn, activeEvents)),
                recipientContinuity = "none",
                initiatorLastOpener = CapturedOrFallback(pawnCapture?.lastOpener,
                    () => DiaryContextBuilder.LatestDiaryOpener(pawnId, activeEvents), true),
                recipientLastOpener = string.Empty,
                initiatorPreviousEntryEnding = CapturedOrFallback(pawnCapture?.previousEntryEnding,
                    () => DiaryContextBuilder.LatestDiaryEnding(pawnId, activeEvents), true),
                recipientPreviousEntryEnding = string.Empty,
                initiatorWeapon = CapturedOrFallback(pawnCapture?.weapon,
                    () => DiaryContextBuilder.EquippedWeapon(pawn), true),
                recipientWeapon = string.Empty,
                solo = true,
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            ApplyCapturedOrLiveGenerationEligibility(
                diaryEvent, DiaryEvent.InitiatorRole, pawn, pawnCapture);
            ApplyBeliefContext(diaryEvent, DiaryEvent.InitiatorRole, pawn,
                beliefEvidence, preparedBelief);
            diaryEvent.SetStaggeredIntensity(DiaryEvent.InitiatorRole,
                pawnCapture?.staggeredIntensity ?? PawnFactCapture.StaggeredIntensity(pawn));
            diaryEvent.SetTextDecorationFacts(DiaryEvent.InitiatorRole,
                pawnCapture?.textDecorationFacts ?? PawnFactCapture.TextDecorationFacts(pawn));
            events.Register(diaryEvent);
            AddEventRef(pawn, diaryEvent.eventId, historicalTick >= 0);
            ApplyDiaryEventLimits();
            // Associative memory: recall BEFORE deposit so this event cannot recall its own fragment.
            ApplyMemoryContextForEvent(diaryEvent);
            DepositMemoryFragments(diaryEvent);
            if (diaryEvent.IsSkipped(DiaryEvent.InitiatorRole))
            {
                NotifyEntryStatusChanged(diaryEvent, DiaryEvent.InitiatorRole);
            }

            return diaryEvent;
        }

        private static void ApplyBeliefContext(
            DiaryEvent diaryEvent,
            string povRole,
            Pawn pawn,
            BeliefEventEvidence evidence,
            BeliefContextBuildResult prepared)
        {
            if (diaryEvent == null || pawn == null || evidence == null && prepared == null)
                return;
            BeliefContextBuildResult result = prepared ?? BeliefContextBuilder.Build(
                pawn, evidence, diaryEvent.eventId, diaryEvent.tick, povRole);
            diaryEvent.SetBeliefContext(povRole, result?.fullContext);
        }

        private static BirthWriterContextSnapshot FindBirthWriterContext(
            BirthEventContextSnapshot capturedContext,
            string pawnId)
        {
            if (capturedContext?.writers == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            for (int i = 0; i < capturedContext.writers.Count; i++)
            {
                BirthWriterContextSnapshot row = capturedContext.writers[i];
                if (row != null && string.Equals(row.pawnId, pawnId, StringComparison.Ordinal))
                {
                    return row;
                }
            }

            return null;
        }

        private static string CapturedOrLiveName(BirthWriterContextSnapshot captured, Pawn pawn)
        {
            return captured != null && !string.IsNullOrWhiteSpace(captured.displayName)
                ? captured.displayName
                : pawn.LabelShortCap;
        }

        private static string CapturedOrFallback(
            string captured,
            Func<string> fallback,
            bool blankIsCaptured = false)
        {
            if (captured != null && (blankIsCaptured || !string.IsNullOrWhiteSpace(captured)))
            {
                return captured;
            }

            return fallback == null ? string.Empty : fallback();
        }

        private static int ResolveEventTick(int historicalTick)
        {
            return historicalTick >= 0 ? historicalTick : Find.TickManager.TicksGame;
        }

        private static string ResolveEventDate(
            BirthEventContextSnapshot capturedContext,
            int historicalTick)
        {
            if (!string.IsNullOrWhiteSpace(capturedContext?.birthDate))
            {
                return capturedContext.birthDate;
            }

            if (historicalTick < 0)
            {
                return GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Vector2.zero);
            }

            int elapsed = Math.Max(0, Find.TickManager.TicksGame - historicalTick);
            long absolute = Math.Max(0L, (long)Find.TickManager.TicksAbs - elapsed);
            return GenDate.DateFullStringAt(
                absolute > int.MaxValue ? int.MaxValue : (int)absolute,
                Vector2.zero);
        }

        private static void ApplyCapturedOrLiveGenerationEligibility(
            DiaryEvent diaryEvent,
            string povRole,
            Pawn pawn,
            BirthWriterContextSnapshot captured)
        {
            if (captured == null)
            {
                MarkIncapacitatedPovSkipped(diaryEvent, povRole, pawn);
                return;
            }

            if (captured.skipFirstPersonGeneration)
            {
                diaryEvent.MarkSkipped(povRole, IncapacitatedSkipReason());
            }
        }
    }
}

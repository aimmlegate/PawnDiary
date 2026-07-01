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
            IReadOnlyList<DiaryEvent> activeEvents = ActiveScanEvents();
            string neutralText = string.Equals(initiatorText, recipientText, StringComparison.OrdinalIgnoreCase)
                ? initiatorText
                : initiator.LabelShortCap + ": " + initiatorText + " / " + recipient.LabelShortCap + ": " + recipientText;

            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = Guid.NewGuid().ToString("N"),
                tick = Find.TickManager.TicksGame,
                date = GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Vector2.zero),
                interactionDefName = defName,
                interactionLabel = label,
                initiatorPawnId = initiator.GetUniqueLoadID(),
                recipientPawnId = recipient.GetUniqueLoadID(),
                initiatorName = initiator.LabelShortCap,
                recipientName = recipient.LabelShortCap,
                initiatorText = initiatorText,
                recipientText = recipientText,
                neutralText = neutralText,
                gameContext = gameContext,
                instruction = instruction,
                colorCue = DiaryEvent.ResolveColorCue(defName, gameContext),
                initiatorPawnSummary = DiaryContextBuilder.BuildPawnSummary(initiator),
                recipientPawnSummary = DiaryContextBuilder.BuildPawnSummary(recipient),
                initiatorSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(initiator),
                recipientSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(recipient),
                initiatorContinuity = DiaryContextBuilder.BuildContinuitySummary(initiator, recipient, activeEvents),
                recipientContinuity = DiaryContextBuilder.BuildContinuitySummary(recipient, initiator, activeEvents),
                initiatorLastOpener = DiaryContextBuilder.LatestDiaryOpener(initiator.GetUniqueLoadID(), activeEvents),
                recipientLastOpener = DiaryContextBuilder.LatestDiaryOpener(recipient.GetUniqueLoadID(), activeEvents),
                initiatorPreviousEntryEnding = DiaryContextBuilder.LatestDiaryEnding(initiator.GetUniqueLoadID(), activeEvents),
                recipientPreviousEntryEnding = DiaryContextBuilder.LatestDiaryEnding(recipient.GetUniqueLoadID(), activeEvents),
                initiatorWeapon = DiaryContextBuilder.EquippedWeapon(initiator),
                recipientWeapon = DiaryContextBuilder.EquippedWeapon(recipient),
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            MarkIncapacitatedPovSkipped(diaryEvent, DiaryEvent.InitiatorRole, initiator);
            MarkIncapacitatedPovSkipped(diaryEvent, DiaryEvent.RecipientRole, recipient);
            // Snapshot display-only pawn facts (staggered handwriting, text-decoration hediff/trait
            // names) from the live pawns here, then store plain values on the model. After this the
            // DiaryEvent never touches Pawn state. See PawnFactCapture / AGENTS.md ("barrier").
            diaryEvent.SetStaggeredIntensity(DiaryEvent.InitiatorRole, PawnFactCapture.StaggeredIntensity(initiator));
            diaryEvent.SetStaggeredIntensity(DiaryEvent.RecipientRole, PawnFactCapture.StaggeredIntensity(recipient));
            diaryEvent.SetTextDecorationFacts(DiaryEvent.InitiatorRole, PawnFactCapture.TextDecorationFacts(initiator));
            diaryEvent.SetTextDecorationFacts(DiaryEvent.RecipientRole, PawnFactCapture.TextDecorationFacts(recipient));
            events.Register(diaryEvent);
            AddEventRef(initiator, diaryEvent.eventId);
            AddEventRef(recipient, diaryEvent.eventId);
            ApplyDiaryEventLimits();
            return diaryEvent;
        }

        /// <summary>
        /// Creates a solo DiaryEvent (single-POV, e.g. a mental break) with no recipient role.
        /// </summary>
        internal DiaryEvent AddSoloEvent(Pawn pawn, Pawn otherPawn, string defName, string label,
            string text, string instruction, string gameContext)
        {
            IReadOnlyList<DiaryEvent> activeEvents = ActiveScanEvents();
            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = Guid.NewGuid().ToString("N"),
                tick = Find.TickManager.TicksGame,
                date = GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Vector2.zero),
                interactionDefName = defName,
                interactionLabel = label,
                initiatorPawnId = pawn.GetUniqueLoadID(),
                recipientPawnId = string.Empty,
                initiatorName = pawn.LabelShortCap,
                recipientName = string.Empty,
                initiatorText = text,
                recipientText = string.Empty,
                neutralText = text,
                gameContext = gameContext,
                instruction = instruction,
                colorCue = DiaryEvent.ResolveColorCue(defName, gameContext),
                initiatorPawnSummary = DiaryContextBuilder.BuildPawnSummary(pawn),
                recipientPawnSummary = "n/a",
                initiatorSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(pawn),
                recipientSurroundings = "n/a",
                initiatorContinuity = DiaryContextBuilder.BuildContinuitySummary(pawn, otherPawn, activeEvents),
                recipientContinuity = "none",
                initiatorLastOpener = DiaryContextBuilder.LatestDiaryOpener(pawn.GetUniqueLoadID(), activeEvents),
                recipientLastOpener = string.Empty,
                initiatorPreviousEntryEnding = DiaryContextBuilder.LatestDiaryEnding(pawn.GetUniqueLoadID(), activeEvents),
                recipientPreviousEntryEnding = string.Empty,
                initiatorWeapon = DiaryContextBuilder.EquippedWeapon(pawn),
                recipientWeapon = string.Empty,
                solo = true,
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            MarkIncapacitatedPovSkipped(diaryEvent, DiaryEvent.InitiatorRole, pawn);
            diaryEvent.SetStaggeredIntensity(DiaryEvent.InitiatorRole, PawnFactCapture.StaggeredIntensity(pawn));
            diaryEvent.SetTextDecorationFacts(DiaryEvent.InitiatorRole, PawnFactCapture.TextDecorationFacts(pawn));
            events.Register(diaryEvent);
            AddEventRef(pawn, diaryEvent.eventId);
            ApplyDiaryEventLimits();
            return diaryEvent;
        }
    }
}

// The orchestrator. Owns the saved diary data and drives the whole flow: record an event
// (RecordInteraction / RecordMentalState) -> build a DiaryEvent with context -> queue an LLM
// request -> apply the result each tick -> hand views to the UI. Context/prompt building live in
// DiaryContextBuilder / DiaryPromptBuilder; the saved data models in DiaryEvent / PawnDiaryRecord.
// This is a RimWorld GameComponent (lifecycle hooks: GameComponentTick, ExposeData, etc.).
// New to C#/RimWorld? See CSHARP-NOTES.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Central coordinator for the diary system: records events, queues LLM generation, applies
    /// finished results each tick, persists everything on save/load, and serves entry views to
    /// the UI. Reach the live instance via <see cref="Current"/>.
    /// </summary>
    public class DiaryGameComponent : GameComponent
    {
        // Dedup windows live in DiaryTuningDef (editable XML); see DiaryTuning.Current.
        private const string SmallTalkGroupKey = "smalltalk";
        private const string SmallTalkBatchDefName = "SmallTalkBatch";
        private const string SmallTalkBatchLabel = "Small talk";

        private List<PawnDiaryRecord> diaries = new List<PawnDiaryRecord>();
        private List<DiaryEvent> diaryEvents = new List<DiaryEvent>();
        private readonly Dictionary<string, PendingSmallTalkBatch> pendingSmallTalkBatches = new Dictionary<string, PendingSmallTalkBatch>();

        // Transient (not saved) guard against duplicate mental-state events, e.g. the two
        // mirrored SocialFighting starts, or a break that re-triggers quickly.
        private readonly Dictionary<string, int> recentMentalEvents = new Dictionary<string, int>();

        public DiaryGameComponent(Game game)
        {
            LlmClient.BeginSession();
        }

        public static DiaryGameComponent Current
        {
            get
            {
                return Verse.Current.Game?.GetComponent<DiaryGameComponent>();
            }
        }

        public override void StartedNewGame()
        {
            pendingSmallTalkBatches.Clear();
            LlmClient.BeginSession();
        }

        public override void LoadedGame()
        {
            pendingSmallTalkBatches.Clear();
            LlmClient.BeginSession();
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                FlushAllSmallTalkBatches();
            }

            Scribe_Collections.Look(ref diaries, "diaries", LookMode.Deep);
            Scribe_Collections.Look(ref diaryEvents, "diaryEvents", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (diaries == null)
                {
                    diaries = new List<PawnDiaryRecord>();
                }

                if (diaryEvents == null)
                {
                    diaryEvents = new List<DiaryEvent>();
                }
            }
        }

        public override void GameComponentTick()
        {
            FlushReadySmallTalkBatches();

            LlmGenerationResult result;
            while (LlmClient.TryDequeueCompleted(out result))
            {
                ApplyLlmResult(result);
            }
        }

        /// <summary>
        /// Returns the diary entries to render for a pawn (newest data first as stored), and
        /// lazily queues generation for any entry that hasn't been written yet. Called by the UI.
        /// </summary>
        public IReadOnlyList<DiaryEntryView> EntriesFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return EmptyEntries.List;
            }

            string pawnId = pawn.GetUniqueLoadID();
            FlushSmallTalkBatchesForPawn(pawnId);

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary == null)
            {
                return EmptyEntries.List;
            }

            List<DiaryEntryView> views = new List<DiaryEntryView>();

            if (diary.eventIds != null)
            {
                for (int i = 0; i < diary.eventIds.Count; i++)
                {
                    DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
                    string povRole = diaryEvent?.RoleForPawn(pawnId);
                    EnsureGenerationQueued(diaryEvent, povRole);
                    DiaryEntryView view = diaryEvent?.ToViewFor(pawnId);
                    if (view != null)
                    {
                        views.Add(view);
                    }
                }
            }

            if (diary.entries != null)
            {
                for (int i = 0; i < diary.entries.Count; i++)
                {
                    DiaryEntryView view = DiaryEntryView.FromLegacy(diary.entries[i]);
                    if (view != null)
                    {
                        views.Add(view);
                    }
                }
            }

            return views;
        }

        public bool DiaryGenerationEnabledFor(Pawn pawn)
        {
            return FindDiary(pawn, true)?.diaryGenerationEnabled ?? true;
        }

        // Public setters/getters are used by the pawn inspector tab. They create the record on
        // first edit so choices exist before the pawn has any diary entries.
        public void SetDiaryGenerationEnabled(Pawn pawn, bool enabled)
        {
            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary != null)
            {
                diary.diaryGenerationEnabled = enabled;
            }
        }

        public DiaryPersonaDef PersonaFor(Pawn pawn)
        {
            PawnDiaryRecord diary = FindDiary(pawn, true);
            return DiaryPersonas.Resolve(diary?.personaDefName);
        }

        public void SetPersona(Pawn pawn, string personaDefName)
        {
            DiaryPersonaDef persona = DiaryPersonas.Resolve(personaDefName);
            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary != null)
            {
                diary.personaDefName = persona.defName;
            }
        }

        private void EnsureGenerationQueued(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            // Disabled pawns still keep the raw event in their diary, but never start an LLM job.
            if (!DiaryGenerationEnabledFor(diaryEvent, povRole))
            {
                return;
            }

            // In paired mode, the recipient entry waits for the finished initiator entry so the
            // second prompt can use it as continuity context.
            if (DualPovEnabled && DiaryEvent.RoleIsInitiatorOrRecipient(povRole) && !diaryEvent.solo)
            {
                QueueSequentialPairwiseRewrite(diaryEvent);
                return;
            }

            if (diaryEvent.CanQueueGeneration(povRole))
            {
                QueueLlmRewrite(diaryEvent, povRole);
            }
        }

        private static bool DualPovEnabled
        {
            get
            {
                return PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.dualPovGeneration;
            }
        }

        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef)
        {
            if (initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            string fallbackText = $"{initiator.LabelShortCap} had a {interactionDef.LabelCap.Resolve()} interaction with {recipient.LabelShortCap}.";
            RecordInteraction(initiator, recipient, interactionDef, fallbackText);
        }

        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef, string gameText)
        {
            RecordInteraction(initiator, recipient, interactionDef, gameText, gameText);
        }

        /// <summary>
        /// Records one social interaction (the full entry point used by the Harmony patch). Skips
        /// it unless its group is enabled, then builds a pairwise <see cref="DiaryEvent"/> and
        /// queues generation. The shorter overloads above just fill in default text.
        /// </summary>
        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef, string initiatorGameText, string recipientGameText)
        {
            if (initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            if (!IsInteractionSignificant(interactionDef))
            {
                return;
            }

            string interactionLabel = interactionDef.LabelCap.Resolve();
            string initiatorText = DiaryContextBuilder.CleanLine(initiatorGameText);
            string recipientText = DiaryContextBuilder.CleanLine(recipientGameText);
            if (string.IsNullOrWhiteSpace(initiatorText))
            {
                initiatorText = $"{initiator.LabelShortCap} had a {interactionLabel} interaction with {recipient.LabelShortCap}.";
            }

            if (string.IsNullOrWhiteSpace(recipientText))
            {
                recipientText = initiatorText;
            }

            if (IsSmallTalkInteraction(interactionDef))
            {
                RecordSmallTalkInteraction(initiator, recipient, interactionDef, interactionLabel, initiatorText, recipientText);
                return;
            }

            DiaryEvent diaryEvent = AddPairwiseEvent(initiator, recipient, interactionDef.defName, interactionLabel,
                initiatorText, recipientText,
                InteractionInstruction(interactionDef),
                DiaryContextBuilder.BuildGameContextSummary(interactionDef, interactionLabel));
            QueuePairwiseGeneration(diaryEvent);
        }

        private void RecordSmallTalkInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef,
            string interactionLabel, string initiatorText, string recipientText)
        {
            string key = PairKey(initiator, recipient);
            PendingSmallTalkBatch batch;
            if (!pendingSmallTalkBatches.TryGetValue(key, out batch))
            {
                int now = Find.TickManager.TicksGame;
                batch = new PendingSmallTalkBatch
                {
                    initiator = initiator,
                    recipient = recipient,
                    initiatorPawnId = initiator.GetUniqueLoadID(),
                    recipientPawnId = recipient.GetUniqueLoadID(),
                    firstTick = now,
                    lastTick = now,
                    firstDefName = interactionDef.defName,
                    firstLabel = interactionLabel,
                    instruction = InteractionInstruction(interactionDef)
                };
                pendingSmallTalkBatches[key] = batch;
            }

            AppendSmallTalkBatchLine(batch, initiator, interactionLabel, initiatorText, recipientText);
            batch.lastTick = Find.TickManager.TicksGame;

            if (batch.Count >= SmallTalkBatchMaxEvents)
            {
                FlushSmallTalkBatch(key, batch);
            }
        }

        // Social fights and mental breaks. Social fights are pairwise (both pawns fight);
        // everything else is recorded as a solo break from the breaking pawn's point of view.
        public void RecordMentalState(Pawn pawn, MentalStateDef stateDef, Pawn otherPawn, string reason)
        {
            if (!IsHumanlike(pawn) || stateDef == null)
            {
                return;
            }

            if (PawnDiaryMod.Settings == null || !PawnDiaryMod.Settings.IsMentalStateEnabled(stateDef))
            {
                return;
            }

            string label = stateDef.LabelCap.Resolve();
            string instruction = PawnDiaryMod.Settings.InstructionForMentalState(stateDef);
            bool isSocialFight = string.Equals(stateDef.defName, "SocialFighting", StringComparison.OrdinalIgnoreCase)
                && IsHumanlike(otherPawn) && otherPawn != pawn;

            if (isSocialFight)
            {
                // SocialFighting fires once per participant; collapse the mirrored call.
                if (RecentlyRecorded("fight|" + PairKey(pawn, otherPawn), DiaryTuning.Current.socialFightDedupTicks))
                {
                    return;
                }

                string text = pawn.LabelShortCap + " and " + otherPawn.LabelShortCap + " lost their tempers and came to blows.";
                string gameContext = "mental_state=" + stateDef.defName + "; label=" + DiaryContextBuilder.CleanLine(label) + ReasonSuffix(reason);
                DiaryEvent fightEvent = AddPairwiseEvent(pawn, otherPawn, stateDef.defName, label,
                    text, text, instruction, gameContext);
                QueuePairwiseGeneration(fightEvent);
                return;
            }

            if (RecentlyRecorded("break|" + pawn.GetUniqueLoadID() + "|" + stateDef.defName, DiaryTuning.Current.mentalBreakDedupTicks))
            {
                return;
            }

            string breakText = BuildMentalBreakText(pawn, label, otherPawn, reason);
            string breakContext = "mental_state=" + stateDef.defName + "; label=" + DiaryContextBuilder.CleanLine(label)
                + (otherPawn != null ? "; target=" + DiaryContextBuilder.CleanLine(otherPawn.LabelShortCap) : string.Empty)
                + ReasonSuffix(reason);
            DiaryEvent breakEvent = AddSoloEvent(pawn, otherPawn, stateDef.defName, label, breakText, instruction, breakContext);
            QueueLlmRewrite(breakEvent, DiaryEvent.InitiatorRole);
        }

        private DiaryEvent AddPairwiseEvent(Pawn initiator, Pawn recipient, string defName, string label,
            string initiatorText, string recipientText, string instruction, string gameContext)
        {
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
                sequenceText = neutralText,
                gameContext = gameContext,
                instruction = instruction,
                initiatorPawnSummary = DiaryContextBuilder.BuildPawnSummary(initiator),
                recipientPawnSummary = DiaryContextBuilder.BuildPawnSummary(recipient),
                initiatorSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(initiator),
                recipientSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(recipient),
                opinionsSummary = DiaryContextBuilder.BuildOpinionsSummary(initiator, recipient),
                initiatorContinuity = DiaryContextBuilder.BuildContinuitySummary(initiator, recipient, diaryEvents),
                recipientContinuity = DiaryContextBuilder.BuildContinuitySummary(recipient, initiator, diaryEvents),
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            diaryEvents.Add(diaryEvent);
            AddEventRef(initiator, diaryEvent.eventId);
            AddEventRef(recipient, diaryEvent.eventId);
            return diaryEvent;
        }

        private DiaryEvent AddSoloEvent(Pawn pawn, Pawn otherPawn, string defName, string label,
            string text, string instruction, string gameContext)
        {
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
                sequenceText = text,
                gameContext = gameContext,
                instruction = instruction,
                initiatorPawnSummary = DiaryContextBuilder.BuildPawnSummary(pawn),
                recipientPawnSummary = "n/a",
                initiatorSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(pawn),
                recipientSurroundings = "n/a",
                opinionsSummary = otherPawn != null ? DiaryContextBuilder.BuildOpinionsSummary(pawn, otherPawn) : "n/a",
                initiatorContinuity = DiaryContextBuilder.BuildContinuitySummary(pawn, otherPawn, diaryEvents),
                recipientContinuity = "none",
                solo = true,
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            diaryEvents.Add(diaryEvent);
            AddEventRef(pawn, diaryEvent.eventId);
            return diaryEvent;
        }

        private void QueuePairwiseGeneration(DiaryEvent diaryEvent)
        {
            if (DualPovEnabled)
            {
                QueueSequentialPairwiseRewrite(diaryEvent);
            }
            else
            {
                QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
            }
        }

        private static string BuildMentalBreakText(Pawn pawn, string label, Pawn otherPawn, string reason)
        {
            string text = pawn.LabelShortCap + " had a mental break (" + DiaryContextBuilder.CleanLine(label) + ")";
            if (otherPawn != null)
            {
                text += ", directed at " + DiaryContextBuilder.CleanLine(otherPawn.LabelShortCap);
            }

            string cleanReason = DiaryContextBuilder.CleanLine(reason);
            if (!string.IsNullOrWhiteSpace(cleanReason))
            {
                text += ". Reason: " + cleanReason;
            }

            return text + ".";
        }

        private static string ReasonSuffix(string reason)
        {
            string cleanReason = DiaryContextBuilder.CleanLine(reason);
            return string.IsNullOrWhiteSpace(cleanReason) ? string.Empty : "; reason=" + cleanReason;
        }

        private bool RecentlyRecorded(string key, int windowTicks)
        {
            int now = Find.TickManager.TicksGame;
            int last;
            if (recentMentalEvents.TryGetValue(key, out last) && now - last < windowTicks)
            {
                return true;
            }

            recentMentalEvents[key] = now;
            return false;
        }

        private static bool IsHumanlike(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike;
        }

        private void FlushReadySmallTalkBatches()
        {
            if (pendingSmallTalkBatches.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            int windowTicks = SmallTalkBatchWindowTicks;
            List<string> keysToFlush = new List<string>();

            foreach (KeyValuePair<string, PendingSmallTalkBatch> pair in pendingSmallTalkBatches)
            {
                PendingSmallTalkBatch batch = pair.Value;
                if (batch == null || batch.Count >= SmallTalkBatchMaxEvents || now - batch.lastTick >= windowTicks)
                {
                    keysToFlush.Add(pair.Key);
                }
            }

            FlushSmallTalkBatches(keysToFlush);
        }

        private void FlushAllSmallTalkBatches()
        {
            if (pendingSmallTalkBatches.Count == 0)
            {
                return;
            }

            FlushSmallTalkBatches(new List<string>(pendingSmallTalkBatches.Keys));
        }

        private void FlushSmallTalkBatchesForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || pendingSmallTalkBatches.Count == 0)
            {
                return;
            }

            List<string> keysToFlush = new List<string>();
            foreach (KeyValuePair<string, PendingSmallTalkBatch> pair in pendingSmallTalkBatches)
            {
                PendingSmallTalkBatch batch = pair.Value;
                if (batch != null && (batch.initiatorPawnId == pawnId || batch.recipientPawnId == pawnId))
                {
                    keysToFlush.Add(pair.Key);
                }
            }

            FlushSmallTalkBatches(keysToFlush);
        }

        private void FlushSmallTalkBatches(List<string> keysToFlush)
        {
            if (keysToFlush == null)
            {
                return;
            }

            for (int i = 0; i < keysToFlush.Count; i++)
            {
                PendingSmallTalkBatch batch;
                if (pendingSmallTalkBatches.TryGetValue(keysToFlush[i], out batch))
                {
                    FlushSmallTalkBatch(keysToFlush[i], batch);
                }
            }
        }

        private void FlushSmallTalkBatch(string key, PendingSmallTalkBatch batch)
        {
            pendingSmallTalkBatches.Remove(key);

            if (batch == null || batch.initiator == null || batch.recipient == null || batch.Count == 0)
            {
                return;
            }

            bool combined = batch.Count > 1;
            string label = combined ? SmallTalkBatchLabel : batch.firstLabel;
            string defName = combined ? SmallTalkBatchDefName : batch.firstDefName;
            string initiatorText = BuildSmallTalkBatchText(batch.initiatorLines);
            string recipientText = BuildSmallTalkBatchText(batch.recipientLines);
            string instruction = SmallTalkBatchInstruction(batch.instruction);
            string gameContext = "group=smalltalk; events=" + batch.Count
                + "; first_tick=" + batch.firstTick
                + "; last_tick=" + batch.lastTick;

            DiaryEvent diaryEvent = AddPairwiseEvent(batch.initiator, batch.recipient, defName, label,
                initiatorText, recipientText, instruction, gameContext);
            QueuePairwiseGeneration(diaryEvent);
        }

        private static void AppendSmallTalkBatchLine(PendingSmallTalkBatch batch, Pawn initiator,
            string interactionLabel, string initiatorText, string recipientText)
        {
            string initiatorLine = SmallTalkBatchLine(interactionLabel, initiatorText);
            string recipientLine = SmallTalkBatchLine(interactionLabel, recipientText);

            if (initiator.GetUniqueLoadID() == batch.initiatorPawnId)
            {
                batch.initiatorLines.Add(initiatorLine);
                batch.recipientLines.Add(recipientLine);
            }
            else
            {
                batch.initiatorLines.Add(recipientLine);
                batch.recipientLines.Add(initiatorLine);
            }
        }

        private static string BuildSmallTalkBatchText(List<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return "A brief small-talk exchange.";
            }

            if (lines.Count == 1)
            {
                return lines[0];
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("Several small-talk moments happened:");
            for (int i = 0; i < lines.Count; i++)
            {
                builder.Append("\n").Append(i + 1).Append(". ").Append(lines[i]);
            }

            return builder.ToString();
        }

        private static string SmallTalkBatchLine(string interactionLabel, string text)
        {
            string cleanText = DiaryContextBuilder.CleanLine(text);
            string cleanLabel = DiaryContextBuilder.CleanLine(interactionLabel);
            if (string.IsNullOrWhiteSpace(cleanLabel))
            {
                return cleanText;
            }

            return cleanLabel + ": " + cleanText;
        }

        private static string SmallTalkBatchInstruction(string baseInstruction)
        {
            string instruction = DiaryContextBuilder.CleanLine(baseInstruction);
            string batchingInstruction = "write one diary entry for the overall small-talk exchange, not separate entries for each listed moment";
            if (string.IsNullOrWhiteSpace(instruction))
            {
                return batchingInstruction;
            }

            return instruction + "; " + batchingInstruction;
        }

        private static int SmallTalkBatchWindowTicks
        {
            get
            {
                return Math.Max(0, DiaryTuning.Current.smallTalkBatchWindowTicks);
            }
        }

        private static int SmallTalkBatchMaxEvents
        {
            get
            {
                return Math.Max(1, DiaryTuning.Current.smallTalkBatchMaxEvents);
            }
        }

        private static string PairKey(Pawn first, Pawn second)
        {
            string firstId = first.GetUniqueLoadID();
            string secondId = second.GetUniqueLoadID();
            return string.CompareOrdinal(firstId, secondId) <= 0
                ? firstId + "|" + secondId
                : secondId + "|" + firstId;
        }

        private void AddEventRef(Pawn pawn, string eventId)
        {
            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null || string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            diary.pawnName = pawn.LabelShortCap;
            if (diary.eventIds == null)
            {
                diary.eventIds = new List<string>();
            }

            if (!diary.eventIds.Contains(eventId))
            {
                diary.eventIds.Add(eventId);
            }
        }

        private void QueueLlmRewrite(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole))
            {
                return;
            }

            if (!diaryEvent.CanQueueGeneration(povRole))
            {
                return;
            }

            // Persona is resolved at queue time so changing a pawn affects future generations
            // without rewriting prompts that were already sent or saved for debugging.
            string rawText = DiaryPromptBuilder.BuildInteractionPrompt(diaryEvent, povRole, PersonaRuleFor(diaryEvent, povRole));
            QueuePrompt(diaryEvent, povRole, rawText);
        }

        private void QueueSequentialPairwiseRewrite(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null || diaryEvent.solo)
            {
                return;
            }

            bool initiatorEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.InitiatorRole);
            bool recipientEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.RecipientRole);

            // Normal paired flow: initiator writes first, then recipient can receive that entry
            // as hidden continuity context.
            if (initiatorEnabled && diaryEvent.CanQueueGeneration(DiaryEvent.InitiatorRole))
            {
                string rawText = DiaryPromptBuilder.BuildSequentialInteractionPrompt(diaryEvent, DiaryEvent.InitiatorRole, PersonaRuleFor(diaryEvent, DiaryEvent.InitiatorRole));
                QueuePrompt(diaryEvent, DiaryEvent.InitiatorRole, rawText);
                return;
            }

            // If the recipient is disabled, stop here even if the initiator completed.
            if (!recipientEnabled)
            {
                return;
            }

            // Keep the old paired behavior when the initiator was supposed to generate but failed.
            if (initiatorEnabled && string.Equals(diaryEvent.initiatorStatus, DiaryEvent.FailedStatus, StringComparison.OrdinalIgnoreCase))
            {
                if (diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
                {
                    diaryEvent.MarkFailed(DiaryEvent.RecipientRole, "Skipped because initiator diary generation failed.");
                }

                return;
            }

            // Wait for initiator context only when the initiator is enabled. If the initiator is
            // disabled, the recipient can still generate from the base event prompt.
            if (initiatorEnabled
                && (!string.Equals(diaryEvent.initiatorStatus, DiaryEvent.CompleteStatus, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(diaryEvent.initiatorGeneratedText)))
            {
                return;
            }

            if (diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
            {
                // Recipient prompt includes hidden initiator context only when that context exists.
                string rawText = initiatorEnabled
                    ? DiaryPromptBuilder.BuildSequentialInteractionPrompt(diaryEvent, DiaryEvent.RecipientRole, PersonaRuleFor(diaryEvent, DiaryEvent.RecipientRole))
                    : DiaryPromptBuilder.BuildInteractionPrompt(diaryEvent, DiaryEvent.RecipientRole, PersonaRuleFor(diaryEvent, DiaryEvent.RecipientRole));
                QueuePrompt(diaryEvent, DiaryEvent.RecipientRole, rawText);
            }
        }

        private void QueuePrompt(DiaryEvent diaryEvent, string povRole, string rawText)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return;
            }

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole))
            {
                return;
            }

            if (!diaryEvent.CanQueueGeneration(povRole))
            {
                return;
            }

            diaryEvent.SetPrompt(povRole, rawText);

            if (PawnDiaryMod.Settings == null)
            {
                diaryEvent.MarkFailed(povRole, "LLM settings are not available.");
                return;
            }

            diaryEvent.llmEndpoint = EndpointUtility.BuildChatCompletionsUrl(PawnDiaryMod.Settings.endpointUrl);
            diaryEvent.llmModel = PawnDiaryMod.Settings.modelName;
            diaryEvent.MarkQueued(povRole);

            LlmClient.Enqueue(new LlmGenerationRequest
            {
                eventId = diaryEvent.eventId,
                povRole = povRole,
                systemPrompt = PawnDiaryMod.Settings.systemPrompt,
                rawText = rawText,
                endpointUrl = PawnDiaryMod.Settings.endpointUrl,
                modelName = PawnDiaryMod.Settings.modelName,
                apiKey = PawnDiaryMod.Settings.apiKey,
                timeoutSeconds = PawnDiaryMod.Settings.timeoutSeconds,
                maxTokens = PawnDiaryMod.Settings.maxTokens,
                temperature = PawnDiaryMod.Settings.temperature
            });
        }


        private static string InteractionInstruction(InteractionDef interactionDef)
        {
            return PawnDiaryMod.Settings?.InstructionFor(interactionDef) ?? string.Empty;
        }

        // An interaction is recorded only if its group (see InteractionGroups) is enabled
        // in settings.
        private static bool IsInteractionSignificant(InteractionDef interactionDef)
        {
            return interactionDef != null
                && !string.IsNullOrWhiteSpace(interactionDef.defName)
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsInteractionEnabled(interactionDef);
        }

        private static bool IsSmallTalkInteraction(InteractionDef interactionDef)
        {
            // Only the low-stakes Small talk group batches. Heartfelt events such as DeepTalk
            // stay immediate because their group key is "heartfelt", not "smalltalk".
            DiaryInteractionGroupDef group = InteractionGroups.Classify(interactionDef);
            return group != null && string.Equals(group.defName, SmallTalkGroupKey, StringComparison.OrdinalIgnoreCase);
        }


        private void ApplyLlmResult(LlmGenerationResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.eventId))
            {
                return;
            }

            DiaryEvent diaryEvent = FindEvent(result.eventId);
            if (diaryEvent == null)
            {
                return;
            }

            diaryEvent.ApplyLlmResult(result);
            QueueRecipientAfterInitiatorResult(diaryEvent, result);
        }

        private void QueueRecipientAfterInitiatorResult(DiaryEvent diaryEvent, LlmGenerationResult result)
        {
            if (!DualPovEnabled
                || diaryEvent == null
                || diaryEvent.solo
                || result == null
                || !string.Equals(result.povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!result.success)
            {
                // Do not mark a disabled recipient as failed; they intentionally have no LLM state.
                if (DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.RecipientRole)
                    && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
                {
                    diaryEvent.MarkFailed(DiaryEvent.RecipientRole, "Skipped because initiator diary generation failed.");
                }

                return;
            }

            QueueSequentialPairwiseRewrite(diaryEvent);
        }

        private bool DiaryGenerationEnabledFor(DiaryEvent diaryEvent, string povRole)
        {
            // DiaryEvents store pawn IDs, not Pawn instances, so queue-time checks resolve through
            // the saved diary record for that POV.
            string pawnId = PawnIdForRole(diaryEvent, povRole);
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return true;
            }

            return FindDiaryByPawnId(pawnId)?.diaryGenerationEnabled ?? true;
        }

        private string PersonaRuleFor(DiaryEvent diaryEvent, string povRole)
        {
            // Missing records fall back to the XML default persona, which also covers old saves.
            string pawnId = PawnIdForRole(diaryEvent, povRole);
            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            return DiaryPersonas.RuleFor(diary?.personaDefName);
        }

        private static string PawnIdForRole(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(povRole))
            {
                return null;
            }

            if (string.Equals(povRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.recipientPawnId;
            }

            if (string.Equals(povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.initiatorPawnId;
            }

            return null;
        }

        private DiaryEvent FindEvent(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId) || diaryEvents == null)
            {
                return null;
            }

            for (int i = 0; i < diaryEvents.Count; i++)
            {
                if (diaryEvents[i].eventId == eventId)
                {
                    return diaryEvents[i];
                }
            }

            return null;
        }

        private PawnDiaryRecord FindDiary(Pawn pawn, bool createIfMissing)
        {
            if (pawn == null)
            {
                return null;
            }

            string pawnId = pawn.GetUniqueLoadID();
            for (int i = 0; i < diaries.Count; i++)
            {
                if (diaries[i].pawnId == pawnId)
                {
                    EnsurePawnDiaryDefaults(diaries[i]);
                    return diaries[i];
                }
            }

            if (!createIfMissing)
            {
                return null;
            }

            PawnDiaryRecord diary = new PawnDiaryRecord
            {
                pawnId = pawnId,
                pawnName = pawn.LabelShortCap,
                personaDefName = DiaryPersonas.Default.defName,
                diaryGenerationEnabled = true
            };
            diaries.Add(diary);
            return diary;
        }

        private PawnDiaryRecord FindDiaryByPawnId(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || diaries == null)
            {
                return null;
            }

            for (int i = 0; i < diaries.Count; i++)
            {
                if (diaries[i].pawnId == pawnId)
                {
                    EnsurePawnDiaryDefaults(diaries[i]);
                    return diaries[i];
                }
            }

            return null;
        }

        private static void EnsurePawnDiaryDefaults(PawnDiaryRecord diary)
        {
            if (diary == null)
            {
                return;
            }

            // Existing saves may have no persona, and XML mods may remove an old persona Def.
            if (string.IsNullOrWhiteSpace(diary.personaDefName) || DiaryPersonas.ForDefName(diary.personaDefName) == null)
            {
                diary.personaDefName = DiaryPersonas.Default.defName;
            }
        }

        private static class EmptyEntries
        {
            public static readonly IReadOnlyList<DiaryEntryView> List = new List<DiaryEntryView>();
        }

        private class PendingSmallTalkBatch
        {
            public Pawn initiator;
            public Pawn recipient;
            public string initiatorPawnId;
            public string recipientPawnId;
            public int firstTick;
            public int lastTick;
            public string firstDefName;
            public string firstLabel;
            public string instruction;
            public readonly List<string> initiatorLines = new List<string>();
            public readonly List<string> recipientLines = new List<string>();

            public int Count
            {
                get
                {
                    return initiatorLines.Count;
                }
            }
        }
    }
}

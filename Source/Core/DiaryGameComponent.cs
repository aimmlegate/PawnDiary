// The orchestrator. Owns the saved diary data and drives the whole flow: record an event
// (RecordInteraction / RecordMentalState) -> build a DiaryEvent with context -> queue an LLM
// request -> apply the result each tick -> hand views to the UI. Context/prompt building live in
// DiaryContextBuilder / DiaryPromptBuilder; the saved data models in DiaryEvent / PawnDiaryRecord.
// This is a RimWorld GameComponent (lifecycle hooks: GameComponentTick, ExposeData, etc.).
// New to C#/RimWorld? See AGENTS.md.
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
        // InteractionGroups key that marks an interaction as low-stakes small talk eligible for batching.
        private const string SmallTalkGroupKey = "smalltalk";
        // Synthetic defName/label used when multiple small-talk interactions are merged into one diary event.
        private const string SmallTalkBatchDefName = "SmallTalkBatch";

        // Per-pawn saved state (event references, persona, enabled flag). Persisted via ExposeData.
        private List<PawnDiaryRecord> diaries = new List<PawnDiaryRecord>();
        // All diary events across every pawn. Persisted via ExposeData.
        private List<DiaryEvent> diaryEvents = new List<DiaryEvent>();
        // Small-talk batches still accumulating lines; keyed by PairKey. Not saved (flushed first).
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
            nextGenerationScanTick = 0;
        }

        public override void LoadedGame()
        {
            pendingSmallTalkBatches.Clear();
            LlmClient.BeginSession();
            nextGenerationScanTick = 0;
            QueueAllPendingGenerations();
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

            int now = Find.TickManager.TicksGame;
            if (now >= nextGenerationScanTick)
            {
                nextGenerationScanTick = now + GenerationScanIntervalTicks;
                QueueAllPendingGenerations();
            }

            LlmGenerationResult result;
            while (LlmClient.TryDequeueCompleted(out result))
            {
                ApplyLlmResult(result);
            }
        }

        /// <summary>
        /// Returns the diary entries to render for a pawn (newest data first as stored).
        /// Pure read — no side effects. Generation is driven entirely by the background tick scan.
        /// </summary>
        public IReadOnlyList<DiaryEntryView> EntriesFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return EmptyEntries.List;
            }

            string pawnId = pawn.GetUniqueLoadID();

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

        /// <summary>
        /// Finds the generated diary entry that belongs to a clicked RimWorld social-log row.
        /// Returns null when the event was not recorded, belongs to another pawn, or has not
        /// produced visible LLM text yet; callers should keep vanilla click behavior in that case.
        /// </summary>
        public DiaryEntryView GeneratedEntryForPlayLogEntry(Pawn pawn, int playLogEntryId)
        {
            if (!IsDiaryEligible(pawn) || playLogEntryId < 0)
            {
                return null;
            }

            string pawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary == null || diary.eventIds == null)
            {
                return null;
            }

            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
                if (diaryEvent == null || !diaryEvent.MatchesPlayLogEntry(playLogEntryId))
                {
                    continue;
                }

                DiaryEntryView view = diaryEvent.ToViewFor(pawnId);
                if (view != null && !string.IsNullOrWhiteSpace(view.GeneratedText))
                {
                    return view;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns whether a pawn has diary generation enabled (defaults to true if no record exists yet).
        /// </summary>
        public bool DiaryGenerationEnabledFor(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn))
            {
                return false;
            }

            return FindDiary(pawn, true)?.diaryGenerationEnabled ?? true;
        }

        public void SetDiaryGenerationEnabled(Pawn pawn, bool enabled)
        {
            if (!IsDiaryEligible(pawn))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary != null)
            {
                diary.diaryGenerationEnabled = enabled;
                if (enabled)
                {
                    QueuePendingGenerationsForPawn(pawn.GetUniqueLoadID());
                }
            }
        }

        public DiaryPersonaDef PersonaFor(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn))
            {
                return DiaryPersonas.Default;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            return DiaryPersonas.Resolve(diary?.personaDefName);
        }

        public void SetPersona(Pawn pawn, string personaDefName)
        {
            if (!IsDiaryEligible(pawn))
            {
                return;
            }

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

            if (!DiaryGenerationEnabledFor(diaryEvent, povRole))
            {
                return;
            }

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

        private const int GenerationScanIntervalTicks = 120;
        private int nextGenerationScanTick;

        private void QueueAllPendingGenerations()
        {
            if (diaryEvents == null)
            {
                return;
            }

            for (int i = 0; i < diaryEvents.Count; i++)
            {
                DiaryEvent diaryEvent = diaryEvents[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                if (diaryEvent.CanQueueGeneration(DiaryEvent.InitiatorRole))
                {
                    EnsureGenerationQueued(diaryEvent, DiaryEvent.InitiatorRole);
                }

                if (!diaryEvent.solo && diaryEvent.CanQueueGeneration(DiaryEvent.RecipientRole))
                {
                    EnsureGenerationQueued(diaryEvent, DiaryEvent.RecipientRole);
                }
            }
        }

        private void QueuePendingGenerationsForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            if (diary == null || diary.eventIds == null)
            {
                return;
            }

            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
                if (diaryEvent == null)
                {
                    continue;
                }

                string povRole = diaryEvent.RoleForPawn(pawnId);
                EnsureGenerationQueued(diaryEvent, povRole);
            }
        }

        private static bool DualPovEnabled
        {
            get
            {
                return PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.dualPovGeneration;
            }
        }

        /// <summary>
        /// Convenience overload that auto-generates a fallback description from the pawns and interaction label.
        /// </summary>
        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef)
        {
            if (initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            string fallbackText = "PawnDiary.Event.Interaction".Translate(initiator.LabelShortCap, interactionDef.LabelCap.Resolve(), recipient.LabelShortCap);
            RecordInteraction(initiator, recipient, interactionDef, fallbackText);
        }

        /// <summary>
        /// Convenience overload that uses the same game text for both initiator and recipient.
        /// </summary>
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
            RecordInteraction(initiator, recipient, interactionDef, initiatorGameText, recipientGameText, -1);
        }

        /// <summary>
        /// Records one social interaction and remembers the originating PlayLog id, so a later
        /// click in RimWorld's Social tab can jump back to the generated diary entry.
        /// </summary>
        public void RecordInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef,
            string initiatorGameText, string recipientGameText, int playLogEntryId)
        {
            if (initiator == null || recipient == null || interactionDef == null)
            {
                return;
            }

            if (!IsInteractionSignificant(interactionDef))
            {
                return;
            }

            bool initiatorEligible = IsDiaryEligible(initiator);
            bool recipientEligible = IsDiaryEligible(recipient);

            if (!initiatorEligible && !recipientEligible)
            {
                return;
            }

            string interactionLabel = interactionDef.LabelCap.Resolve();
            string initiatorText = DiaryContextBuilder.CleanLine(initiatorGameText);
            string recipientText = DiaryContextBuilder.CleanLine(recipientGameText);

            if (!initiatorEligible || !recipientEligible)
            {
                Pawn eligiblePawn = initiatorEligible ? initiator : recipient;
                Pawn otherPawn = initiatorEligible ? recipient : initiator;
                string eligibleText = initiatorEligible ? initiatorText : recipientText;
                if (string.IsNullOrWhiteSpace(eligibleText))
                {
                    eligibleText = "PawnDiary.Event.Interaction".Translate(eligiblePawn.LabelShortCap, interactionLabel, otherPawn.LabelShortCap);
                }

                string gameContext = DiaryContextBuilder.BuildGameContextSummary(interactionDef, interactionLabel);
                DiaryEvent soloEvent = AddSoloEvent(eligiblePawn, otherPawn, interactionDef.defName, interactionLabel,
                    eligibleText, InteractionInstruction(interactionDef), gameContext);
                soloEvent.AddPlayLogEntryId(playLogEntryId);
                QueueLlmRewrite(soloEvent, DiaryEvent.InitiatorRole);
                return;
            }

            if (string.IsNullOrWhiteSpace(initiatorText))
            {
                initiatorText = "PawnDiary.Event.Interaction".Translate(initiator.LabelShortCap, interactionLabel, recipient.LabelShortCap);
            }

            if (string.IsNullOrWhiteSpace(recipientText))
            {
                recipientText = initiatorText;
            }

            if (IsSmallTalkInteraction(interactionDef))
            {
                RecordSmallTalkInteraction(initiator, recipient, interactionDef, interactionLabel, initiatorText, recipientText, playLogEntryId);
                return;
            }

            DiaryEvent diaryEvent = AddPairwiseEvent(initiator, recipient, interactionDef.defName, interactionLabel,
                initiatorText, recipientText,
                InteractionInstruction(interactionDef),
                DiaryContextBuilder.BuildGameContextSummary(interactionDef, interactionLabel));
            diaryEvent.AddPlayLogEntryId(playLogEntryId);
            QueuePairwiseGeneration(diaryEvent);
        }

        private void RecordSmallTalkInteraction(Pawn initiator, Pawn recipient, InteractionDef interactionDef,
            string interactionLabel, string initiatorText, string recipientText, int playLogEntryId)
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
            AddPlayLogEntryId(batch.playLogEntryIds, playLogEntryId);
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
            if (!IsDiaryEligible(pawn) || stateDef == null)
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
                && IsDiaryEligible(otherPawn) && otherPawn != pawn;

            if (isSocialFight)
            {
                // SocialFighting fires once per participant; collapse the mirrored call.
                if (RecentlyRecorded("fight|" + PairKey(pawn, otherPawn), DiaryTuning.Current.socialFightDedupTicks))
                {
                    return;
                }

                string text = "PawnDiary.Event.SocialFight".Translate(pawn.LabelShortCap, otherPawn.LabelShortCap);
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

        /// <summary>
        /// Creates a DiaryEvent involving two pawns (initiator + recipient), enriches it with context
        /// summaries, registers it, and cross-references both pawns' diary records.
        /// </summary>
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
                initiatorLastOpener = DiaryContextBuilder.LatestDiaryOpener(initiator.GetUniqueLoadID(), diaryEvents),
                recipientLastOpener = DiaryContextBuilder.LatestDiaryOpener(recipient.GetUniqueLoadID(), diaryEvents),
                initiatorBurningPassion = DiaryContextBuilder.RandomBurningPassion(initiator),
                recipientBurningPassion = DiaryContextBuilder.RandomBurningPassion(recipient),
                initiatorWeapon = DiaryContextBuilder.EquippedWeapon(initiator),
                recipientWeapon = DiaryContextBuilder.EquippedWeapon(recipient),
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            diaryEvents.Add(diaryEvent);
            AddEventRef(initiator, diaryEvent.eventId);
            AddEventRef(recipient, diaryEvent.eventId);
            return diaryEvent;
        }

        /// <summary>
        /// Creates a solo DiaryEvent (single-POV, e.g. a mental break) with no recipient role.
        /// </summary>
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
                initiatorLastOpener = DiaryContextBuilder.LatestDiaryOpener(pawn.GetUniqueLoadID(), diaryEvents),
                recipientLastOpener = string.Empty,
                initiatorBurningPassion = DiaryContextBuilder.RandomBurningPassion(pawn),
                recipientBurningPassion = string.Empty,
                initiatorWeapon = DiaryContextBuilder.EquippedWeapon(pawn),
                recipientWeapon = string.Empty,
                solo = true,
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };

            diaryEvents.Add(diaryEvent);
            AddEventRef(pawn, diaryEvent.eventId);
            return diaryEvent;
        }

        /// <summary>
        /// Dispatches a pairwise event for LLM generation: sequential if dual-POV is on, else initiator-only.
        /// </summary>
        private void QueuePairwiseGeneration(DiaryEvent diaryEvent)
        {
            if (DualPovEnabled)
            {
                QueueSequentialPairwiseRewrite(diaryEvent);
            }
            else
            {
                QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
                QueueLlmRewrite(diaryEvent, DiaryEvent.RecipientRole);
            }
        }

        /// <summary>
        /// Assembles a human-readable fallback description for a mental break, including target and reason if available.
        /// </summary>
        private static string BuildMentalBreakText(Pawn pawn, string label, Pawn otherPawn, string reason)
        {
            string text = "PawnDiary.Event.MentalBreak".Translate(pawn.LabelShortCap, DiaryContextBuilder.CleanLine(label));
            if (otherPawn != null)
            {
                text += "PawnDiary.Event.DirectedAt".Translate(DiaryContextBuilder.CleanLine(otherPawn.LabelShortCap)).Resolve();
            }

            string cleanReason = DiaryContextBuilder.CleanLine(reason);
            if (!string.IsNullOrWhiteSpace(cleanReason))
            {
                text += "PawnDiary.Event.ReasonSuffix".Translate(cleanReason).Resolve();
            }

            return text + ".";
        }

        /// <summary>
        /// Returns a cleaned "reason=…" suffix for appending to gameContext strings, or empty if no reason.
        /// </summary>
        private static string ReasonSuffix(string reason)
        {
            string cleanReason = DiaryContextBuilder.CleanLine(reason);
            return string.IsNullOrWhiteSpace(cleanReason) ? string.Empty : "; reason=" + cleanReason;
        }

        /// <summary>
        /// Dedup gate: returns true if the same key was recorded within the given tick window, preventing
        /// duplicate events (e.g. mirrored SocialFighting calls).
        /// </summary>
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

        /// <summary>
        /// Checks whether a pawn is a humanlike (as opposed to an animal or mechanoid).
        /// </summary>
        private static bool IsHumanlike(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike;
        }

        /// <summary>
        /// A pawn qualifies for diary tracking if it is a humanlike colonist.
        /// </summary>
        private static bool IsDiaryEligible(Pawn pawn)
        {
            return IsHumanlike(pawn) && pawn.IsColonist;
        }

        /// <summary>
        /// Called each tick: flushes any batch that has exceeded the time window or hit the max-events cap.
        /// </summary>
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

        /// <summary>
        /// Flushes every pending small-talk batch immediately (used on save to avoid losing transient data).
        /// </summary>
        private void FlushAllSmallTalkBatches()
        {
            if (pendingSmallTalkBatches.Count == 0)
            {
                return;
            }

            FlushSmallTalkBatches(new List<string>(pendingSmallTalkBatches.Keys));
        }

        /// <summary>
        /// Flushes each batch identified by its key in the provided list.
        /// </summary>
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

        /// <summary>
        /// Converts a finished batch into a DiaryEvent (solo or pairwise depending on eligibility) and
        /// queues it for LLM generation.
        /// </summary>
        private void FlushSmallTalkBatch(string key, PendingSmallTalkBatch batch)
        {
            pendingSmallTalkBatches.Remove(key);

            if (batch == null || batch.initiator == null || batch.recipient == null || batch.Count == 0)
            {
                return;
            }

            bool initiatorEligible = IsDiaryEligible(batch.initiator);
            bool recipientEligible = IsDiaryEligible(batch.recipient);

            if (!initiatorEligible && !recipientEligible)
            {
                return;
            }

            bool combined = batch.Count > 1;
            string label = combined ? "PawnDiary.Event.SmallTalkLabel".Translate().Resolve() : batch.firstLabel;
            string defName = combined ? SmallTalkBatchDefName : batch.firstDefName;
            string initiatorText = BuildSmallTalkBatchText(batch.initiatorLines);
            string recipientText = BuildSmallTalkBatchText(batch.recipientLines);
            string instruction = SmallTalkBatchInstruction(batch.instruction);
            string gameContext = "group=smalltalk; events=" + batch.Count
                + "; first_tick=" + batch.firstTick
                + "; last_tick=" + batch.lastTick;

            if (!initiatorEligible || !recipientEligible)
            {
                Pawn eligiblePawn = initiatorEligible ? batch.initiator : batch.recipient;
                Pawn otherPawn = initiatorEligible ? batch.recipient : batch.initiator;
                string eligibleText = initiatorEligible ? initiatorText : recipientText;
                if (string.IsNullOrWhiteSpace(eligibleText))
                {
                    eligibleText = "PawnDiary.Event.SmallTalk".Translate(eligiblePawn.LabelShortCap, otherPawn.LabelShortCap);
                }

                DiaryEvent soloEvent = AddSoloEvent(eligiblePawn, otherPawn, defName, label,
                    eligibleText, instruction, gameContext);
                AddPlayLogEntryIds(soloEvent, batch.playLogEntryIds);
                QueueLlmRewrite(soloEvent, DiaryEvent.InitiatorRole);
                return;
            }

            DiaryEvent diaryEvent = AddPairwiseEvent(batch.initiator, batch.recipient, defName, label,
                initiatorText, recipientText, instruction, gameContext);
            AddPlayLogEntryIds(diaryEvent, batch.playLogEntryIds);
            QueuePairwiseGeneration(diaryEvent);
        }

        /// <summary>
        /// Appends one interaction line to the batch, assigning it to the correct initiator/recipient line lists
        /// regardless of which pawn was the initiator in the original interaction.
        /// </summary>
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

        /// <summary>
        /// Adds one PlayLog id to an in-progress small-talk batch, ignoring the sentinel used
        /// by older call paths that do not know the originating log row.
        /// </summary>
        private static void AddPlayLogEntryId(List<int> playLogEntryIds, int playLogEntryId)
        {
            if (playLogEntryIds == null || playLogEntryId < 0 || playLogEntryIds.Contains(playLogEntryId))
            {
                return;
            }

            playLogEntryIds.Add(playLogEntryId);
        }

        /// <summary>
        /// Copies all PlayLog ids from a small-talk batch onto its merged diary event.
        /// </summary>
        private static void AddPlayLogEntryIds(DiaryEvent diaryEvent, List<int> playLogEntryIds)
        {
            if (diaryEvent == null || playLogEntryIds == null)
            {
                return;
            }

            for (int i = 0; i < playLogEntryIds.Count; i++)
            {
                diaryEvent.AddPlayLogEntryId(playLogEntryIds[i]);
            }
        }

        /// <summary>
        /// Formats accumulated lines into a single description string; uses a numbered list when multiple.
        /// </summary>
        private static string BuildSmallTalkBatchText(List<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return "PawnDiary.Event.SmallTalkBrief".Translate();
            }

            if (lines.Count == 1)
            {
                return lines[0];
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("PawnDiary.Event.SmallTalkBatchHeader".Translate().Resolve());
            for (int i = 0; i < lines.Count; i++)
            {
                builder.Append("\n").Append(i + 1).Append(". ").Append(lines[i]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Formats one small-talk moment as "label: text" (or just text if the label is empty).
        /// </summary>
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

        /// <summary>
        /// Appends a batching-specific instruction to the base instruction so the LLM writes one combined entry.
        /// </summary>
        private static string SmallTalkBatchInstruction(string baseInstruction)
        {
            string instruction = DiaryContextBuilder.CleanLine(baseInstruction);
            string batchingInstruction = "PawnDiary.Event.SmallTalkBatchInstruction".Translate();
            if (string.IsNullOrWhiteSpace(instruction))
            {
                return batchingInstruction;
            }

            return instruction + "; " + batchingInstruction;
        }

        /// <summary>
        /// How many ticks a small-talk batch stays open before being flushed automatically.
        /// </summary>
        private static int SmallTalkBatchWindowTicks
        {
            get
            {
                return Math.Max(0, DiaryTuning.Current.smallTalkBatchWindowTicks);
            }
        }

        /// <summary>
        /// Maximum number of small-talk interactions to accumulate per batch before forcing a flush.
        /// </summary>
        private static int SmallTalkBatchMaxEvents
        {
            get
            {
                return Math.Max(1, DiaryTuning.Current.smallTalkBatchMaxEvents);
            }
        }

        /// <summary>
        /// Produces a deterministic, order-independent key for a pawn pair (for small-talk batch grouping).
        /// </summary>
        private static string PairKey(Pawn first, Pawn second)
        {
            string firstId = first.GetUniqueLoadID();
            string secondId = second.GetUniqueLoadID();
            return string.CompareOrdinal(firstId, secondId) <= 0
                ? firstId + "|" + secondId
                : secondId + "|" + firstId;
        }

        /// <summary>
        /// Adds an event ID to a pawn's diary, creating the diary record if necessary.
        /// </summary>
        private void AddEventRef(Pawn pawn, string eventId)
        {
            if (!IsDiaryEligible(pawn) || string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null)
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

        /// <summary>
        /// Builds the prompt for a single POV role and enqueues the LLM request if generation is still allowed.
        /// </summary>
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

        /// <summary>
        /// Dual-POV flow: queues the initiator first, then the recipient once the initiator result arrives
        /// (so the recipient prompt can include the initiator's generated text as hidden continuity context).
        /// </summary>
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
                    diaryEvent.MarkFailed(DiaryEvent.RecipientRole, "PawnDiary.Error.SkippedInitiatorFailed".Translate());
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

        /// <summary>
        /// Final step before LLM dispatch: stamps the prompt on the event, records endpoint metadata, marks
        /// the event queued, and enqueues the request to <see cref="LlmClient"/>.
        /// </summary>
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
                diaryEvent.MarkFailed(povRole, "PawnDiary.Error.NoLlmSettings".Translate());
                return;
            }

            // Pick which configured API lane handles this request. New events spread across all
            // lanes (parallelism); the recipient half of a paired event reuses the initiator's lane
            // so a sequential pair stays on one model.
            List<ApiEndpointConfig> targets = PawnDiaryMod.Settings.ActiveEndpoints();
            if (targets.Count == 0)
            {
                diaryEvent.MarkFailed(povRole, "PawnDiary.Error.NoApiConfigured".Translate());
                return;
            }

            ApiEndpointConfig target = SelectApiTarget(diaryEvent, povRole, targets);

            diaryEvent.SetLlmMeta(povRole, EndpointUtility.BuildChatCompletionsUrl(target.url), target.model);
            diaryEvent.MarkQueued(povRole);

            LlmClient.Enqueue(new LlmGenerationRequest
            {
                eventId = diaryEvent.eventId,
                povRole = povRole,
                systemPrompt = PawnDiaryMod.Settings.systemPrompt,
                rawText = rawText,
                endpointUrl = target.url,
                modelName = target.model,
                apiKey = target.apiKey,
                // The other configured lanes, tried in order if this one errors ("use next model").
                failoverTargets = BuildFailoverTargets(targets, target),
                timeoutSeconds = PawnDiaryMod.Settings.timeoutSeconds,
                maxTokens = PawnDiaryMod.Settings.maxTokens,
                temperature = PawnDiaryMod.Settings.temperature
            });
        }

        /// <summary>
        /// Chooses the primary API lane for a request from the given active lanes (non-empty).
        /// The recipient of a paired (dual-POV) event reuses the lane the initiator used — matched
        /// by the endpoint+model recorded on the event — so the sequential pair shares one model.
        /// Everything else takes the next lane in round-robin order to spread load across APIs.
        /// </summary>
        private static ApiEndpointConfig SelectApiTarget(DiaryEvent diaryEvent, string povRole, List<ApiEndpointConfig> targets)
        {
            // Sequential pinning: keep the recipient on the initiator's lane when paired mode ran
            // the initiator first and recorded which endpoint+model it used (after failover, the
            // recorded meta is the lane that actually produced the initiator's entry).
            if (DualPovEnabled && DiaryEvent.RoleEquals(povRole, DiaryEvent.RecipientRole))
            {
                string initiatorEndpoint = diaryEvent.initiatorLlmEndpoint;
                string initiatorModel = diaryEvent.initiatorLlmModel;
                if (!string.IsNullOrWhiteSpace(initiatorEndpoint) && !string.IsNullOrWhiteSpace(initiatorModel))
                {
                    foreach (ApiEndpointConfig candidate in targets)
                    {
                        if (string.Equals(EndpointUtility.BuildChatCompletionsUrl(candidate.url), initiatorEndpoint, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(candidate.model, initiatorModel, StringComparison.Ordinal))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return targets[LlmClient.NextRoundRobinIndex() % targets.Count];
        }

        /// <summary>
        /// Returns the lanes to fall back to (every active lane except the chosen primary), in order,
        /// so a request that errors on its primary lane can retry on the others.
        /// </summary>
        private static List<ApiEndpointConfig> BuildFailoverTargets(List<ApiEndpointConfig> targets, ApiEndpointConfig primary)
        {
            List<ApiEndpointConfig> failovers = new List<ApiEndpointConfig>();
            foreach (ApiEndpointConfig candidate in targets)
            {
                if (!ReferenceEquals(candidate, primary))
                {
                    failovers.Add(candidate);
                }
            }

            return failovers;
        }


        /// <summary>
        /// Returns the user-defined instruction for a specific interaction def, or empty string if none.
        /// </summary>
        private static string InteractionInstruction(InteractionDef interactionDef)
        {
            return PawnDiaryMod.Settings?.InstructionFor(interactionDef) ?? string.Empty;
        }

        /// <summary>
        /// An interaction is recorded only if its group (see InteractionGroups) is enabled in settings.
        /// </summary>
        private static bool IsInteractionSignificant(InteractionDef interactionDef)
        {
            return interactionDef != null
                && !string.IsNullOrWhiteSpace(interactionDef.defName)
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsInteractionEnabled(interactionDef);
        }

        /// <summary>
        /// Returns true when the interaction belongs to the "smalltalk" group and should be batched rather
        /// than recorded individually.
        /// </summary>
        private static bool IsSmallTalkInteraction(InteractionDef interactionDef)
        {
            // Only the low-stakes Small talk group batches. Heartfelt events such as DeepTalk
            // stay immediate because their group key is "heartfelt", not "smalltalk".
            DiaryInteractionGroupDef group = InteractionGroups.Classify(interactionDef);
            return group != null && string.Equals(group.defName, SmallTalkGroupKey, StringComparison.OrdinalIgnoreCase);
        }


        /// <summary>
        /// Dequeues a completed LLM result and applies it to the corresponding DiaryEvent, then kicks
        /// off the recipient side if dual-POV is active.
        /// </summary>
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

            // Record the lane that actually produced the text. After failover this may differ from
            // the primary lane chosen at queue time, so updating it here keeps the debug block
            // accurate and lets a paired recipient pin to the model the initiator really used.
            if (result.success && !string.IsNullOrWhiteSpace(result.endpointUrl) && !string.IsNullOrWhiteSpace(result.modelName))
            {
                diaryEvent.SetLlmMeta(result.povRole, EndpointUtility.BuildChatCompletionsUrl(result.endpointUrl), result.modelName);
            }

            QueueRecipientAfterInitiatorResult(diaryEvent, result);
        }

        /// <summary>
        /// After the initiator entry completes, either marks the recipient as failed (if the initiator failed)
        /// or re-evaluates the sequential queue so the recipient can generate with the initiator's text as context.
        /// </summary>
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
                    diaryEvent.MarkFailed(DiaryEvent.RecipientRole, "PawnDiary.Error.SkippedInitiatorFailed".Translate());
                }

                return;
            }

            QueueSequentialPairwiseRewrite(diaryEvent);
        }

        /// <summary>
        /// Checks whether the pawn for a given POV role in an event has diary generation enabled,
        /// resolving the pawn via its saved diary record.
        /// </summary>
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

        /// <summary>
        /// Resolves the LLM persona rule string for a given POV in an event, falling back to the XML default.
        /// </summary>
        private string PersonaRuleFor(DiaryEvent diaryEvent, string povRole)
        {
            // Missing records fall back to the XML default persona, which also covers old saves.
            string pawnId = PawnIdForRole(diaryEvent, povRole);
            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            return DiaryPersonas.RuleFor(diary?.personaDefName);
        }

        /// <summary>
        /// Returns the pawn ID for a given POV role in a DiaryEvent (initiator or recipient).
        /// </summary>
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

        /// <summary>
        /// Locates a DiaryEvent by its unique ID.
        /// </summary>
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

        /// <summary>
        /// Public accessor for finding a DiaryEvent by ID (used by the diary tab to build
        /// linked-entry previews and navigate to the other pawn's entry).
        /// </summary>
        public DiaryEvent FindEventById(string eventId)
        {
            return FindEvent(eventId);
        }

        /// <summary>
        /// Looks up a pawn's diary record by Pawn instance; optionally creates one if missing.
        /// </summary>
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
                personaDefName = DiaryPersonas.RandomStartingPersona().defName,
                diaryGenerationEnabled = true
            };
            diaries.Add(diary);
            return diary;
        }

        /// <summary>
        /// Looks up a diary record by pawn ID string (no creation).
        /// </summary>
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

        /// <summary>
        /// Migrates old saves: ensures a persona is assigned even if the Def was removed or never set.
        /// </summary>
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

        /// <summary>
        /// Singleton empty list returned when a pawn has no diary entries, avoiding per-call allocations.
        /// </summary>
        private static class EmptyEntries
        {
            public static readonly IReadOnlyList<DiaryEntryView> List = new List<DiaryEntryView>();
        }

        /// <summary>
        /// Accumulates small-talk moments between a specific pair of pawns until the batch window
        /// expires or the max event count is reached, then produces a single merged DiaryEvent.
        /// </summary>
        private class PendingSmallTalkBatch
        {
            // Live Pawn references — only valid during the current game session (not saved).
            public Pawn initiator;
            public Pawn recipient;
            // Saved-safe IDs used when flushing after a Pawn reference may have become invalid.
            public string initiatorPawnId;
            public string recipientPawnId;
            // Tick range over which the batch accumulated lines.
            public int firstTick;
            public int lastTick;
            // The defName/label of the first interaction in the batch, used as the event label
            // when the batch contains only one entry.
            public string firstDefName;
            public string firstLabel;
            // The LLM instruction carried over from the first interaction in the batch.
            public string instruction;
            // Per-POV line accumulators — each small-talk moment appends one line per POV.
            public readonly List<string> initiatorLines = new List<string>();
            public readonly List<string> recipientLines = new List<string>();
            // RimWorld social-log ids represented by the eventual merged diary event.
            public readonly List<int> playLogEntryIds = new List<int>();

            /// <summary>Number of small-talk moments accumulated so far.</summary>
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

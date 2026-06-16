// Small-talk batching. Low-stakes chatter (the "smalltalk" interaction group) would flood the
// diary one line at a time, so instead each pawn-pair accumulates a PendingSmallTalkBatch keyed by
// PairKey. RecordSmallTalkInteraction (called from RecordInteraction in
// DiaryGameComponent.Interactions.cs when the interaction is small talk) opens/appends to the batch;
// a batch is flushed — turned into one merged DiaryEvent — when it hits the max-events cap or its
// time window lapses (checked each tick via FlushReadySmallTalkBatches), and unconditionally on save
// (FlushAllSmallTalkBatches) so transient batches are never lost. The rest are helpers that format
// the merged text/instruction and carry the originating PlayLog ids onto the event.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Opens or appends to the pending small-talk batch for this pawn pair. Called by
        /// RecordInteraction when the interaction belongs to the low-stakes "smalltalk" group, so the
        /// chatter is merged into one diary entry instead of flooding the diary line by line.
        /// </summary>
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

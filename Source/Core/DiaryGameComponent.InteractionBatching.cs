// XML-configured interaction batching. Some social PlayLog rows can fire in bursts (small talk,
// repeated chatter from mods, etc.); creating one diary entry per row floods the diary and the LLM
// queue. Interaction groups can opt into a <batch> policy in DiaryInteractionGroupDefs.xml. Matching
// rows accumulate here by group + pawn pair (and optionally InteractionDef), then flush into one
// normal DiaryEvent when the quiet window lapses, the max count is reached, or the game saves.
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
        /// Returns the classified interaction group when that group has an enabled batch policy.
        /// </summary>
        private static DiaryInteractionGroupDef BatchGroupFor(InteractionDef interactionDef)
        {
            DiaryInteractionGroupDef group = InteractionGroups.Classify(interactionDef);
            return group != null && group.HasBatchPolicy ? group : null;
        }

        /// <summary>
        /// Opens or appends to the pending interaction batch for this group/pawn pair.
        /// </summary>
        private void RecordBatchedInteraction(DiaryInteractionGroupDef group, Pawn initiator, Pawn recipient,
            InteractionDef interactionDef, string interactionLabel, string initiatorText, string recipientText,
            int playLogEntryId)
        {
            if (group == null || group.batch == null)
            {
                return;
            }

            string key = InteractionBatchKey(group, interactionDef, initiator, recipient);
            PendingInteractionBatch batch;
            if (!pendingInteractionBatches.TryGetValue(key, out batch))
            {
                int now = Find.TickManager.TicksGame;
                batch = new PendingInteractionBatch
                {
                    group = group,
                    policy = group.batch,
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
                pendingInteractionBatches[key] = batch;
            }

            AppendInteractionBatchLine(batch, initiator, interactionLabel, initiatorText, recipientText);
            AddPlayLogEntryId(batch.playLogEntryIds, playLogEntryId);
            batch.lastTick = Find.TickManager.TicksGame;

            if (batch.Count >= BatchMaxEvents(batch.policy))
            {
                FlushInteractionBatch(key, batch);
            }
        }

        /// <summary>
        /// Called each tick: flushes batches that exceeded their policy's quiet window or max count.
        /// </summary>
        private void FlushReadyInteractionBatches()
        {
            if (pendingInteractionBatches.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            List<string> keysToFlush = new List<string>();

            foreach (KeyValuePair<string, PendingInteractionBatch> pair in pendingInteractionBatches)
            {
                PendingInteractionBatch batch = pair.Value;
                if (batch == null
                    || batch.Count >= BatchMaxEvents(batch.policy)
                    || now - batch.lastTick >= BatchWindowTicks(batch.policy))
                {
                    keysToFlush.Add(pair.Key);
                }
            }

            FlushInteractionBatches(keysToFlush);
        }

        /// <summary>
        /// Flushes every pending interaction batch immediately (used on save to avoid data loss).
        /// </summary>
        private void FlushAllInteractionBatches()
        {
            if (pendingInteractionBatches.Count == 0)
            {
                return;
            }

            FlushInteractionBatches(new List<string>(pendingInteractionBatches.Keys));
        }

        /// <summary>
        /// Flushes each batch identified by its key in the provided list.
        /// </summary>
        private void FlushInteractionBatches(List<string> keysToFlush)
        {
            if (keysToFlush == null)
            {
                return;
            }

            for (int i = 0; i < keysToFlush.Count; i++)
            {
                PendingInteractionBatch batch;
                if (pendingInteractionBatches.TryGetValue(keysToFlush[i], out batch))
                {
                    FlushInteractionBatch(keysToFlush[i], batch);
                }
            }
        }

        /// <summary>
        /// Converts a finished batch into a DiaryEvent and queues it for the normal generation flow.
        /// </summary>
        private void FlushInteractionBatch(string key, PendingInteractionBatch batch)
        {
            pendingInteractionBatches.Remove(key);

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
            string label = combined ? BatchLabel(batch) : batch.firstLabel;
            string defName = combined ? BatchDefName(batch) : batch.firstDefName;
            string initiatorText = BuildInteractionBatchText(batch, batch.initiatorLines);
            string recipientText = BuildInteractionBatchText(batch, batch.recipientLines);
            string instruction = BatchInstruction(batch);
            string gameContext = "group=" + batch.GroupKey
                + "; batch=interaction"
                + "; events=" + batch.Count
                + "; first_tick=" + batch.firstTick
                + "; last_tick=" + batch.lastTick;

            if (!initiatorEligible || !recipientEligible)
            {
                Pawn eligiblePawn = initiatorEligible ? batch.initiator : batch.recipient;
                Pawn otherPawn = initiatorEligible ? batch.recipient : batch.initiator;
                string eligibleText = initiatorEligible ? initiatorText : recipientText;
                if (string.IsNullOrWhiteSpace(eligibleText))
                {
                    eligibleText = BatchFallback(batch, eligiblePawn, otherPawn);
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
        /// Appends one interaction line to the batch, assigning it to the correct POV line lists even
        /// if the pawns trade initiator/recipient roles across the batch.
        /// </summary>
        private static void AppendInteractionBatchLine(PendingInteractionBatch batch, Pawn initiator,
            string interactionLabel, string initiatorText, string recipientText)
        {
            bool includeLabel = batch.policy == null || batch.policy.includeInteractionLabel;
            string initiatorLine = InteractionBatchLine(includeLabel ? interactionLabel : null, initiatorText);
            string recipientLine = InteractionBatchLine(includeLabel ? interactionLabel : null, recipientText);

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
        /// Adds one PlayLog id to an in-progress batch, ignoring the sentinel used by older call paths.
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
        /// Copies all PlayLog ids from a batch onto its merged diary event.
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
        private static string BuildInteractionBatchText(PendingInteractionBatch batch, List<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return TranslateBatchKey(batch, batch.policy?.briefKey, "PawnDiary.Event.BatchBrief");
            }

            if (lines.Count == 1)
            {
                return lines[0];
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(TranslateBatchKey(batch, batch.policy?.headerKey, "PawnDiary.Event.BatchHeader"));
            for (int i = 0; i < lines.Count; i++)
            {
                builder.Append("\n").Append(i + 1).Append(". ").Append(lines[i]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Formats one batched moment as "label: text" (or just text if no label should be shown).
        /// </summary>
        private static string InteractionBatchLine(string interactionLabel, string text)
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
        /// Appends a batching-specific instruction so the LLM writes one combined entry.
        /// </summary>
        private static string BatchInstruction(PendingInteractionBatch batch)
        {
            string instruction = DiaryContextBuilder.CleanLine(batch.instruction);
            string batchingInstruction = TranslateBatchKey(batch, batch.policy?.instructionKey,
                "PawnDiary.Event.BatchInstruction");
            if (string.IsNullOrWhiteSpace(instruction))
            {
                return batchingInstruction;
            }

            return instruction + "; " + batchingInstruction;
        }

        /// <summary>
        /// Player-facing label for a combined batch.
        /// </summary>
        private static string BatchLabel(PendingInteractionBatch batch)
        {
            return TranslateBatchKey(batch, batch.policy?.labelKey, "PawnDiary.Event.BatchLabel");
        }

        /// <summary>
        /// Fallback game text when a batch has no usable lines for the eligible pawn.
        /// </summary>
        private static string BatchFallback(PendingInteractionBatch batch, Pawn eligiblePawn, Pawn otherPawn)
        {
            string key = string.IsNullOrWhiteSpace(batch.policy?.fallbackKey)
                ? "PawnDiary.Event.BatchFallback"
                : batch.policy.fallbackKey;
            return key.Translate(eligiblePawn.LabelShortCap, otherPawn.LabelShortCap, BatchGroupLabel(batch)).Resolve();
        }

        /// <summary>
        /// Synthetic defName for a combined batch.
        /// </summary>
        private static string BatchDefName(PendingInteractionBatch batch)
        {
            if (batch.policy != null && !string.IsNullOrWhiteSpace(batch.policy.syntheticDefName))
            {
                return batch.policy.syntheticDefName;
            }

            return batch.GroupKey + "Batch";
        }

        /// <summary>
        /// Shared translator for policy-specific keys. Generic fallback keys receive the group label as {0}.
        /// </summary>
        private static string TranslateBatchKey(PendingInteractionBatch batch, string policyKey, string fallbackKey)
        {
            string key = string.IsNullOrWhiteSpace(policyKey) ? fallbackKey : policyKey;
            return key.Translate(BatchGroupLabel(batch)).Resolve();
        }

        /// <summary>
        /// Localized group label used in generic batch text.
        /// </summary>
        private static string BatchGroupLabel(PendingInteractionBatch batch)
        {
            return batch.group == null ? string.Empty : batch.group.LabelCap.Resolve();
        }

        /// <summary>
        /// Window in ticks for this batch policy, with the old small-talk tuning as fallback.
        /// </summary>
        private static int BatchWindowTicks(InteractionBatchPolicy policy)
        {
            if (policy != null && policy.windowTicks >= 0)
            {
                return policy.windowTicks;
            }

            return Math.Max(0, DiaryTuning.Current.smallTalkBatchWindowTicks);
        }

        /// <summary>
        /// Maximum event count for this batch policy, with the old small-talk tuning as fallback.
        /// </summary>
        private static int BatchMaxEvents(InteractionBatchPolicy policy)
        {
            if (policy != null && policy.maxEvents > 0)
            {
                return policy.maxEvents;
            }

            return Math.Max(1, DiaryTuning.Current.smallTalkBatchMaxEvents);
        }

        /// <summary>
        /// Produces a deterministic key for the configured batch scope.
        /// </summary>
        private static string InteractionBatchKey(DiaryInteractionGroupDef group, InteractionDef interactionDef,
            Pawn first, Pawn second)
        {
            string key = group.defName;
            if (group.batch != null && group.batch.scope == InteractionBatchScope.Def)
            {
                key += "|" + interactionDef.defName;
            }

            return key + "|" + PairKey(first, second);
        }

        /// <summary>
        /// Produces a deterministic, order-independent key for a pawn pair. Shared by interaction
        /// batches and mirrored social-fight deduplication.
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
        /// Accumulates matching social-log moments until the policy says to flush.
        /// </summary>
        private class PendingInteractionBatch
        {
            public DiaryInteractionGroupDef group;
            public InteractionBatchPolicy policy;
            // Live Pawn references — only valid during the current game session (not saved).
            public Pawn initiator;
            public Pawn recipient;
            // Saved-safe IDs used for stable pair matching while this in-memory batch exists.
            public string initiatorPawnId;
            public string recipientPawnId;
            // Tick range over which the batch accumulated lines.
            public int firstTick;
            public int lastTick;
            // First event identity, used when the batch contains only one entry.
            public string firstDefName;
            public string firstLabel;
            // The LLM instruction carried over from the first interaction in the batch.
            public string instruction;
            // Per-POV line accumulators — each moment appends one line per POV.
            public readonly List<string> initiatorLines = new List<string>();
            public readonly List<string> recipientLines = new List<string>();
            // RimWorld social-log ids represented by the eventual merged diary event.
            public readonly List<int> playLogEntryIds = new List<int>();

            public string GroupKey
            {
                get
                {
                    return group == null ? "unknown" : group.defName;
                }
            }

            /// <summary>Number of social-log moments accumulated so far.</summary>
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

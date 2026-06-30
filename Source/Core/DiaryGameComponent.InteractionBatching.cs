// XML-configured interaction batching. Some social PlayLog rows can fire in bursts (small talk,
// repeated chatter from mods, etc.); creating one diary entry per row floods the diary and the LLM
// queue. Interaction groups can opt into a <batch> policy in DiaryInteractionGroupDefs.xml. Matching
// rows either accumulate here by group + pawn pair (and optionally InteractionDef), then flush into
// one normal pairwise DiaryEvent; or, for AmbientDayNote mode, accumulate per pawn/day and flush
// into one solo diary memory that uses chatter as background texture instead of a log.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Returns the classified interaction group when that group has an enabled batch policy.
        /// </summary>
        internal static DiaryInteractionGroupDef BatchGroupFor(InteractionDef interactionDef)
        {
            DiaryInteractionGroupDef group = InteractionGroups.Classify(interactionDef);
            return group != null && group.HasBatchPolicy ? group : null;
        }

        /// <summary>
        /// Returns the per-group prompt instruction for a specific interaction def, or empty string if
        /// none. Used by the batch flush when it builds the final batched entry.
        /// </summary>
        private static string InteractionInstruction(InteractionDef interactionDef)
        {
            return InteractionGroups.InstructionFor(interactionDef);
        }

        /// <summary>
        /// Weighted-random gate: returns true when this otherwise-batched moment should "win"
        /// promotion to its own immediate pairwise diary event instead of being merged into the
        /// group's batch. Higher odds when the pair's feelings are intense/lopsided or when a pawn
        /// is in an extreme need/mood state. No-op (false) for groups without a promotion policy.
        /// </summary>
        internal static bool ShouldPromoteInteraction(DiaryInteractionGroupDef group, Pawn initiator, Pawn recipient)
        {
            if (group == null || !group.HasPromotionPolicy || initiator == null || recipient == null)
            {
                return false;
            }

            float weight = PawnDiarySettings.ClampGenerationChanceWeight(
                PawnDiaryMod.Settings?.generationChanceWeight ?? PawnDiarySettings.DefaultGenerationChanceWeight);
            return Rand.Chance(Mathf.Clamp01(PromotionChance(group.promotion, initiator, recipient) * weight));
        }

        /// <summary>
        /// Builds the promotion probability: a small base chance plus a bonus for each notable
        /// signal, clamped to the policy's ceiling. Reads only structured, language-independent
        /// data (opinion numbers, need levels), so it behaves identically in every language.
        /// </summary>
        private static float PromotionChance(InteractionPromotionPolicy promo, Pawn a, Pawn b)
        {
            float chance = promo.baseChance;

            // Social dynamic: intense mutual feeling, or a lopsided one-way bond, both raise interest.
            int opinionAB = a.relations?.OpinionOf(b) ?? 0;
            int opinionBA = b.relations?.OpinionOf(a) ?? 0;
            if (Mathf.Max(Mathf.Abs(opinionAB), Mathf.Abs(opinionBA)) >= promo.opinionStrongThreshold)
            {
                chance += promo.opinionStrongBonus;
            }

            if (Mathf.Abs(opinionAB - opinionBA) >= promo.opinionAsymmetryThreshold)
            {
                chance += promo.opinionAsymmetryBonus;
            }

            // Pawn-state salience: a starving/exhausted/joy-starved pawn, or one near a mental break.
            if (HasLowNeed(a, promo.needLowThreshold) || HasLowNeed(b, promo.needLowThreshold))
            {
                chance += promo.needLowBonus;
            }

            if (IsMoodLow(a, promo.moodLowThreshold) || IsMoodLow(b, promo.moodLowThreshold))
            {
                chance += promo.moodExtremeBonus;
            }

            return Mathf.Clamp(chance, 0f, promo.maxChance);
        }

        /// <summary>
        /// True when any core need (food, rest, joy) sits at or below the threshold fraction (0..1).
        /// Pawns that lack a given need (e.g. animals, certain pawn kinds) simply don't trigger it.
        /// </summary>
        private static bool HasLowNeed(Pawn pawn, float threshold)
        {
            return IsBelow(NeedLevel(pawn?.needs?.food), threshold)
                || IsBelow(NeedLevel(pawn?.needs?.rest), threshold)
                || IsBelow(NeedLevel(pawn?.needs?.joy), threshold);
        }

        /// <summary>True when the pawn's mood need sits at or below the threshold fraction (0..1).</summary>
        private static bool IsMoodLow(Pawn pawn, float threshold)
        {
            return IsBelow(NeedLevel(pawn?.needs?.mood), threshold);
        }

        /// <summary>
        /// Current level of a need as a 0..1 fraction, or -1 when the need is absent. The -1 sentinel
        /// lets <see cref="IsBelow"/> skip missing needs instead of treating them as "empty".
        /// </summary>
        private static float NeedLevel(Need need)
        {
            return need == null ? -1f : need.CurLevelPercentage;
        }

        /// <summary>True only when a real (non-sentinel) need level is at or below the threshold.</summary>
        private static bool IsBelow(float level, float threshold)
        {
            return level >= 0f && level <= threshold;
        }

        /// <summary>
        /// Opens or appends to the pending interaction batch for this group/pawn pair.
        /// </summary>
        internal void RecordBatchedInteraction(DiaryInteractionGroupDef group, Pawn initiator, Pawn recipient,
            InteractionDef interactionDef, string interactionLabel, string initiatorText, string recipientText,
            int playLogEntryId)
        {
            if (group == null || group.batch == null)
            {
                return;
            }

            if (group.batch.mode == InteractionBatchMode.AmbientDayNote)
            {
                RecordAmbientInteraction(group, initiator, recipient, interactionDef, interactionLabel,
                    initiatorText, recipientText, playLogEntryId);
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
            if (pendingInteractionBatches.Count == 0 && pendingAmbientInteractionNotes.Count == 0)
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
            FlushReadyAmbientInteractionNotes(now);
        }

        /// <summary>
        /// Flushes every pending interaction batch immediately (used on save to avoid data loss).
        /// </summary>
        private void FlushAllInteractionBatches()
        {
            if (pendingInteractionBatches.Count == 0 && pendingAmbientInteractionNotes.Count == 0)
            {
                return;
            }

            FlushInteractionBatches(new List<string>(pendingInteractionBatches.Keys));
            FlushAmbientInteractionNotes(new List<string>(pendingAmbientInteractionNotes.Keys));
        }

        /// <summary>
        /// Records low-stakes interaction evidence as solo per-pawn day notes rather than pairwise entries.
        /// </summary>
        private void RecordAmbientInteraction(DiaryInteractionGroupDef group, Pawn initiator, Pawn recipient,
            InteractionDef interactionDef, string interactionLabel, string initiatorText, string recipientText,
            int playLogEntryId)
        {
            if (IsDiaryEligible(initiator))
            {
                AppendAmbientInteraction(group, initiator, recipient, interactionDef, interactionLabel,
                    initiatorText, playLogEntryId);
            }

            if (IsDiaryEligible(recipient))
            {
                AppendAmbientInteraction(group, recipient, initiator, interactionDef, interactionLabel,
                    recipientText, playLogEntryId);
            }
        }

        /// <summary>
        /// Adds one interaction moment to the given pawn's ambient day-note batch.
        /// </summary>
        private void AppendAmbientInteraction(DiaryInteractionGroupDef group, Pawn pawn, Pawn otherPawn,
            InteractionDef interactionDef, string interactionLabel, string pawnText, int playLogEntryId)
        {
            string key = AmbientInteractionKey(group, pawn, CurrentDayIndex);
            if (writtenAmbientInteractionNotes.Contains(key))
            {
                return;
            }

            PendingAmbientInteractionNote note;
            if (!pendingAmbientInteractionNotes.TryGetValue(key, out note))
            {
                int now = Find.TickManager.TicksGame;
                note = new PendingAmbientInteractionNote
                {
                    key = key,
                    group = group,
                    policy = group.batch,
                    pawn = pawn,
                    pawnId = pawn.GetUniqueLoadID(),
                    dayIndex = CurrentDayIndex,
                    firstTick = now,
                    lastTick = now,
                    instruction = InteractionInstruction(interactionDef)
                };
                pendingAmbientInteractionNotes[key] = note;
            }

            note.eventCount++;
            note.lastTick = Find.TickManager.TicksGame;
            AddPlayLogEntryId(note.playLogEntryIds, playLogEntryId);
            AddAmbientParticipant(note, otherPawn);

            if (note.sampleLines.Count < AmbientMaxSampleLines(note.policy))
            {
                note.sampleLines.Add(AmbientInteractionLine(otherPawn, interactionLabel, pawnText));
            }

            if (note.eventCount >= BatchMaxEvents(note.policy))
            {
                FlushAmbientInteractionNote(key, note);
            }
        }

        /// <summary>
        /// Flushes ambient notes when the day changes, their quiet window expires, or max count is reached.
        /// </summary>
        private void FlushReadyAmbientInteractionNotes(int now)
        {
            if (pendingAmbientInteractionNotes.Count == 0)
            {
                return;
            }

            int currentDay = CurrentDayIndex;
            List<string> keysToFlush = new List<string>();
            foreach (KeyValuePair<string, PendingAmbientInteractionNote> pair in pendingAmbientInteractionNotes)
            {
                PendingAmbientInteractionNote note = pair.Value;
                if (note == null
                    || note.dayIndex != currentDay
                    || note.eventCount >= BatchMaxEvents(note.policy)
                    || now - note.lastTick >= BatchWindowTicks(note.policy))
                {
                    keysToFlush.Add(pair.Key);
                }
            }

            FlushAmbientInteractionNotes(keysToFlush);
        }

        /// <summary>
        /// Flushes each ambient day-note identified by its key.
        /// </summary>
        private void FlushAmbientInteractionNotes(List<string> keysToFlush)
        {
            if (keysToFlush == null)
            {
                return;
            }

            for (int i = 0; i < keysToFlush.Count; i++)
            {
                PendingAmbientInteractionNote note;
                if (pendingAmbientInteractionNotes.TryGetValue(keysToFlush[i], out note))
                {
                    FlushAmbientInteractionNote(keysToFlush[i], note);
                }
            }
        }

        /// <summary>
        /// Flushes only this pawn's ambient interaction notes that already meet their minimum count.
        /// Used when the pawn starts resting, so bedtime can become the natural diary-writing moment.
        /// </summary>
        private void FlushAmbientInteractionNotesForPawn(Pawn pawn)
        {
            if (pawn == null || pendingAmbientInteractionNotes.Count == 0)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            List<string> keysToFlush = new List<string>();
            foreach (KeyValuePair<string, PendingAmbientInteractionNote> pair in pendingAmbientInteractionNotes)
            {
                PendingAmbientInteractionNote note = pair.Value;
                if (note != null
                    && string.Equals(note.pawnId, pawnId, StringComparison.Ordinal)
                    && note.eventCount >= AmbientMinEventsToWrite(note.policy))
                {
                    keysToFlush.Add(pair.Key);
                }
            }

            FlushAmbientInteractionNotes(keysToFlush);
        }

        /// <summary>
        /// Turns an ambient day-note batch into one solo diary event, or drops it if it stayed too thin.
        /// </summary>
        private void FlushAmbientInteractionNote(string key, PendingAmbientInteractionNote note)
        {
            pendingAmbientInteractionNotes.Remove(key);

            if (note == null || note.pawn == null || !IsDiaryEligible(note.pawn))
            {
                return;
            }

            if (note.eventCount < AmbientMinEventsToWrite(note.policy))
            {
                return;
            }

            string label = AmbientLabel(note);
            string defName = AmbientDefName(note);
            string text = BuildAmbientInteractionText(note);
            string instruction = AmbientInstruction(note);
            string gameContext = "group=" + note.GroupKey
                + "; batch=ambient_day_note"
                + "; events=" + note.eventCount
                + "; day=" + note.dayIndex
                + "; participants=" + string.Join(", ", note.participantNames.ToArray())
                + "; first_tick=" + note.firstTick
                + "; last_tick=" + note.lastTick;

            DiaryEvent diaryEvent = AddSoloEvent(note.pawn, null, defName, label, text, instruction, gameContext);
            AddPlayLogEntryIds(diaryEvent, note.playLogEntryIds);
            writtenAmbientInteractionNotes.Add(key);
            QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
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
            // defName above may be a synthetic combined-batch name; keep the originating interaction's
            // real def so the generated-speech Social-log row can resolve a valid InteractionDef.
            diaryEvent.playLogInteractionDefName = batch.firstDefName;
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
        /// Formats accumulated lines into a single description string without numeric list markers.
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
                builder.Append("\n").Append("- ").Append(lines[i]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Formats the raw evidence for an ambient note. The LLM instruction tells it not to write a list.
        /// </summary>
        private static string BuildAmbientInteractionText(PendingAmbientInteractionNote note)
        {
            if (note.sampleLines.Count == 0)
            {
                return AmbientFallback(note);
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(TranslateAmbientKey(note, note.policy?.headerKey,
                "PawnDiary.Event.AmbientDayHeader"));
            for (int i = 0; i < note.sampleLines.Count; i++)
            {
                builder.Append("\n").Append("- ").Append(note.sampleLines[i]);
            }

            if (note.eventCount > note.sampleLines.Count)
            {
                builder.Append("\n").Append("... ")
                    .Append("PawnDiary.Event.AmbientDayMore".Translate().Resolve());
            }

            return builder.ToString();
        }

        /// <summary>
        /// Formats one batched moment as "label: text" (or just text if no label should be shown).
        /// </summary>
        private static string InteractionBatchLine(string interactionLabel, string text)
        {
            string cleanText = DiaryLineCleaner.CleanLine(text);
            string cleanLabel = DiaryLineCleaner.CleanLine(interactionLabel);
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
            string instruction = DiaryLineCleaner.CleanLine(batch.instruction);
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
        /// Player-facing label for an ambient day note.
        /// </summary>
        private static string AmbientLabel(PendingAmbientInteractionNote note)
        {
            return TranslateAmbientKey(note, note.policy?.labelKey, "PawnDiary.Event.AmbientDayLabel");
        }

        /// <summary>
        /// Natural-language fallback text for an ambient note with no usable sample lines.
        /// </summary>
        private static string AmbientFallback(PendingAmbientInteractionNote note)
        {
            string key = string.IsNullOrWhiteSpace(note.policy?.fallbackKey)
                ? "PawnDiary.Event.AmbientDayFallback"
                : note.policy.fallbackKey;
            return key.Translate(note.pawn.LabelShortCap, AmbientGroupLabel(note)).Resolve();
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
        /// Synthetic defName for an ambient day note.
        /// </summary>
        private static string AmbientDefName(PendingAmbientInteractionNote note)
        {
            if (note.policy != null && !string.IsNullOrWhiteSpace(note.policy.syntheticDefName))
            {
                return note.policy.syntheticDefName;
            }

            return note.GroupKey + "AmbientDay";
        }

        /// <summary>
        /// Appends the ambient-note instruction that protects the diary illusion.
        /// </summary>
        private static string AmbientInstruction(PendingAmbientInteractionNote note)
        {
            string instruction = DiaryLineCleaner.CleanLine(note.instruction);
            string ambientInstruction = TranslateAmbientKey(note, note.policy?.instructionKey,
                "PawnDiary.Event.AmbientDayInstruction");
            if (string.IsNullOrWhiteSpace(instruction))
            {
                return ambientInstruction;
            }

            return instruction + "; " + ambientInstruction;
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
        /// Shared translator for ambient policy keys. Generic fallback keys receive the group label as {0}.
        /// </summary>
        private static string TranslateAmbientKey(PendingAmbientInteractionNote note, string policyKey, string fallbackKey)
        {
            string key = string.IsNullOrWhiteSpace(policyKey) ? fallbackKey : policyKey;
            return key.Translate(AmbientGroupLabel(note)).Resolve();
        }

        /// <summary>
        /// Localized group label used in generic batch text.
        /// </summary>
        private static string BatchGroupLabel(PendingInteractionBatch batch)
        {
            return batch.group == null ? string.Empty : batch.group.LabelCap.Resolve();
        }

        /// <summary>
        /// Localized group label used in generic ambient note text.
        /// </summary>
        private static string AmbientGroupLabel(PendingAmbientInteractionNote note)
        {
            return note.group == null ? string.Empty : note.group.LabelCap.Resolve();
        }

        /// <summary>
        /// Window in ticks for this batch policy.
        /// </summary>
        private static int BatchWindowTicks(InteractionBatchPolicy policy)
        {
            return Math.Max(0, policy?.windowTicks ?? 0);
        }

        /// <summary>
        /// Maximum event count for this batch policy.
        /// </summary>
        private static int BatchMaxEvents(InteractionBatchPolicy policy)
        {
            return Math.Max(1, policy?.maxEvents ?? 1);
        }

        /// <summary>
        /// Minimum event count before an ambient day note is worth writing.
        /// </summary>
        private static int AmbientMinEventsToWrite(InteractionBatchPolicy policy)
        {
            if (policy != null && policy.minEventsToWrite > 0)
            {
                return policy.minEventsToWrite;
            }

            return 1;
        }

        /// <summary>
        /// Maximum number of evidence lines passed to the LLM for one ambient day note.
        /// </summary>
        private static int AmbientMaxSampleLines(InteractionBatchPolicy policy)
        {
            if (policy != null && policy.maxSampleLines > 0)
            {
                return policy.maxSampleLines;
            }

            return 5;
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
        /// Key for one group's ambient note for one pawn on one in-game day.
        /// </summary>
        private static string AmbientInteractionKey(DiaryInteractionGroupDef group, Pawn pawn, int dayIndex)
        {
            return group.defName + "|ambient|" + pawn.GetUniqueLoadID() + "|" + dayIndex;
        }

        /// <summary>
        /// Adds the other participant to the ambient note once, for prompt context.
        /// </summary>
        private static void AddAmbientParticipant(PendingAmbientInteractionNote note, Pawn otherPawn)
        {
            if (note == null || otherPawn == null)
            {
                return;
            }

            string id = otherPawn.GetUniqueLoadID();
            if (note.participantIds.Contains(id))
            {
                return;
            }

            note.participantIds.Add(id);
            note.participantNames.Add(DiaryLineCleaner.CleanLine(otherPawn.LabelShortCap));
        }

        /// <summary>
        /// Builds one compact evidence line for an ambient note from this pawn's point of view.
        /// </summary>
        private static string AmbientInteractionLine(Pawn otherPawn, string interactionLabel, string text)
        {
            string cleanText = DiaryLineCleaner.CleanLine(text);
            string cleanLabel = DiaryLineCleaner.CleanLine(interactionLabel);
            string otherName = otherPawn == null ? string.Empty : DiaryLineCleaner.CleanLine(otherPawn.LabelShortCap);

            StringBuilder builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(otherName))
            {
                builder.Append("PawnDiary.Ctx.With".Translate(otherName).Resolve());
            }

            if (!string.IsNullOrWhiteSpace(cleanLabel))
            {
                if (builder.Length > 0)
                {
                    builder.Append(" - ");
                }

                builder.Append(cleanLabel);
            }

            if (!string.IsNullOrWhiteSpace(cleanText))
            {
                if (builder.Length > 0)
                {
                    builder.Append(": ");
                }

                builder.Append(cleanText);
            }

            return builder.ToString();
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

        /// <summary>
        /// Accumulates low-stakes interaction evidence for one pawn/day, then writes one solo memory.
        /// </summary>
        private class PendingAmbientInteractionNote
        {
            public string key;
            public DiaryInteractionGroupDef group;
            public InteractionBatchPolicy policy;
            public Pawn pawn;
            public string pawnId;
            public int dayIndex;
            public int firstTick;
            public int lastTick;
            public string instruction;
            public int eventCount;
            public readonly List<string> sampleLines = new List<string>();
            public readonly List<string> participantIds = new List<string>();
            public readonly List<string> participantNames = new List<string>();
            public readonly List<int> playLogEntryIds = new List<int>();

            public string GroupKey
            {
                get
                {
                    return group == null ? "unknown" : group.defName;
                }
            }
        }
    }
}

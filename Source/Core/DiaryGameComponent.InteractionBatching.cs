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
        /// Native weighted-random gate: returns true when this otherwise-batched moment should "win"
        /// promotion to its own immediate pairwise diary event instead of being merged into the
        /// group's batch. Higher odds when the pair's feelings are intense/lopsided or when a pawn
        /// is in an extreme need/mood state. Frequency settings do not alter this routing decision;
        /// the promoted page receives the shared group admission afterward. No-op (false) for groups
        /// without a promotion policy.
        /// </summary>
        internal static bool ShouldPromoteInteraction(DiaryInteractionGroupDef group, Pawn initiator, Pawn recipient)
        {
            if (group == null || !group.HasPromotionPolicy || initiator == null || recipient == null)
            {
                return false;
            }

            // The route is frozen into the capture payload. Isolate this one-shot diary decision so
            // it cannot advance the global seeded RNG used by RimWorld gameplay.
            Rand.PushState();
            try
            {
                return Rand.Chance(PromotionChance(group.promotion, initiator, recipient));
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>
        /// Freezes the one frequency result owned by a newly-opened delayed interaction page. The
        /// pending aggregate keeps this bool until every flush route settles it, so a settings change,
        /// quiet-window flush, save flush, or one-pawn fallback can never reroll the same candidate.
        /// </summary>
        private bool FreezeInteractionAggregateFrequency(
            DiaryInteractionGroupDef group)
        {
            if (group == null || string.IsNullOrWhiteSpace(group.defName))
            {
                return false;
            }

            PawnDiarySettings settings = PawnDiaryMod.Settings;
            float playerOverride = DiaryFrequencyPolicy.StandardMultiplier;
            bool hasOverride = settings != null
                && settings.TryGetRuntimeGroupFrequencyOverride(
                    group.defName,
                    out playerOverride);
            DiaryFrequencyPresetSnapshot preset = settings?.RuntimeFrequencyPresetSnapshot();
            float multiplier = DiaryFrequencyPolicy.ResolveEffectiveMultiplier(
                preset,
                group.defName,
                group.frequencyTier,
                hasOverride,
                playerOverride);

            float effectiveChance;
            bool validChance = DiaryFrequencyPolicy.TryCalculateEffectiveChance(
                1f,
                multiplier,
                out effectiveChance);
            float roll = float.NaN;
            if (validChance && effectiveChance > 0f && effectiveChance < 1f)
            {
                // The result lives in the pending aggregate. Draw from the component-owned stream so
                // consecutive batches evolve independently without advancing RimWorld's gameplay RNG.
                roll = admissionRandom.NextUnitFloat();
            }
            else if (validChance)
            {
                // Deterministic 0x/1x settings do not need even an isolated draw. The pure policy owns
                // the inclusive boundary and explicitly closes zero probability.
                roll = effectiveChance <= 0f ? 0f : 1f;
            }

            return DiaryFrequencyPolicy.Decide(new DiaryFrequencyRequest
            {
                groupKey = group.defName,
                frequencyTier = group.frequencyTier,
                nativeCaptureChance = 1f,
                preset = preset,
                hasPlayerOverride = hasOverride,
                playerOverride = playerOverride,
                enabled = true,
                bypassFrequency = false,
                roll = roll
            }).Accepted;
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
            // TryReadOpinion leaves the value at 0 on a null tracker or a throwing read, matching the
            // old "?? 0" fallback while also surviving vanilla's fragile opinion math.
            int opinionAB;
            TryReadOpinion(a, b, out opinionAB);
            int opinionBA;
            TryReadOpinion(b, a, out opinionBA);
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

            int eventTick = Find.TickManager.TicksGame;
            string key = InteractionBatchKey(group, interactionDef, initiator, recipient);
            PendingInteractionBatch batch;
            if (!pendingInteractionBatches.TryGetValue(key, out batch))
            {
                batch = new PendingInteractionBatch
                {
                    group = group,
                    policy = group.batch,
                    frequencyGroupKey = group.defName ?? string.Empty,
                    frequencyTier = group.frequencyTier ?? string.Empty,
                    frequencyAdmissionAccepted = FreezeInteractionAggregateFrequency(group),
                    initiator = initiator,
                    recipient = recipient,
                    initiatorPawnId = initiator.GetUniqueLoadID(),
                    recipientPawnId = recipient.GetUniqueLoadID(),
                    firstTick = eventTick,
                    lastTick = eventTick,
                    firstDefName = interactionDef.defName,
                    firstLabel = interactionLabel,
                    instruction = InteractionInstruction(interactionDef)
                };
                pendingInteractionBatches[key] = batch;
            }

            // The PlayLog id is the source identity, not merely link metadata. Reject an already-seen
            // row before it can add duplicate prose, replace retained mood, or advance the flush count.
            // Older call paths use -1 when no PlayLog row exists; those remain independently admissible.
            if (!TryAdmitPlayLogEntryId(batch.playLogEntryIds, playLogEntryId))
            {
                return;
            }

            if (batch.Count == 0)
            {
                CaptureFirstBatchTexts(batch, initiator, initiatorText, recipientText);
            }

            AppendInteractionBatchLine(batch, initiator, interactionLabel, initiatorText, recipientText);
            RetainInteractionBatchMood(batch, initiator, recipient, eventTick);
            batch.lastTick = eventTick;

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
            // Allocated lazily: this runs every tick while ANY batch is pending, and most of those
            // ticks nothing is ready yet — the flush helper below treats null as "nothing to flush".
            List<string> keysToFlush = null;

            foreach (KeyValuePair<string, PendingInteractionBatch> pair in pendingInteractionBatches)
            {
                PendingInteractionBatch batch = pair.Value;
                if (batch == null
                    || batch.Count >= BatchMaxEvents(batch.policy)
                    || now - batch.lastTick >= BatchWindowTicks(batch.policy))
                {
                    if (keysToFlush == null)
                    {
                        keysToFlush = new List<string>();
                    }

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
                bool preservedAcceptance = acceptedAmbientInteractionFrequencyKeys != null
                    && acceptedAmbientInteractionFrequencyKeys.Contains(key);
                note = new PendingAmbientInteractionNote
                {
                    key = key,
                    group = group,
                    policy = group.batch,
                    frequencyGroupKey = group.defName ?? string.Empty,
                    frequencyTier = group.frequencyTier ?? string.Empty,
                    frequencyAdmissionAccepted = preservedAcceptance
                        || FreezeInteractionAggregateFrequency(group),
                    pawn = pawn,
                    pawnId = pawn.GetUniqueLoadID(),
                    dayIndex = CurrentDayIndex,
                    firstTick = now,
                    lastTick = now,
                    instruction = InteractionInstruction(interactionDef)
                };
                pendingAmbientInteractionNotes[key] = note;
            }

            // Each eligible POV owns its own ambient note, so admit the source id independently in each
            // note. A replay must be inert before it increments the count or changes samples/mood.
            if (!TryAdmitPlayLogEntryId(note.playLogEntryIds, playLogEntryId))
            {
                return;
            }

            int eventTick = Find.TickManager.TicksGame;
            note.eventCount++;
            note.lastTick = eventTick;
            note.moodSnapshot = MoodSnapshotPolicy.PreferBatchSnapshot(
                note.moodSnapshot,
                DiaryContextBuilder.CaptureMoodSnapshot(pawn, eventTick));
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
            // Allocated lazily: this runs every tick while ANY note is pending, and most of those
            // ticks nothing is ready yet — the flush helper below treats null as "nothing to flush".
            List<string> keysToFlush = null;
            foreach (KeyValuePair<string, PendingAmbientInteractionNote> pair in pendingAmbientInteractionNotes)
            {
                PendingAmbientInteractionNote note = pair.Value;
                if (note == null
                    || note.dayIndex != currentDay
                    || note.eventCount >= BatchMaxEvents(note.policy)
                    || now - note.lastTick >= BatchWindowTicks(note.policy))
                {
                    if (keysToFlush == null)
                    {
                        keysToFlush = new List<string>();
                    }

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

            if (note == null)
            {
                return;
            }

            // A rejected aggregate still settles its pawn/day occurrence. In particular, a pre-save
            // flush must not clear the pending object and then let a later row reopen and reroll the
            // same day's page after saving.
            if (!note.frequencyAdmissionAccepted)
            {
                RememberRejectedAmbientInteractionFrequencyKey(key);
                return;
            }

            // The writer may die after the interaction was captured but before this delayed note flushes.
            if (note.pawn == null || note.pawn.Dead
                || !IsDiaryEligible(note.pawn))
            {
                ForgetAcceptedAmbientInteractionFrequencyKey(key);
                return;
            }

            if (note.eventCount < AmbientMinEventsToWrite(note.policy))
            {
                RememberAcceptedAmbientInteractionFrequencyKey(key);
                return;
            }

            // The pending note was removed above, so establish a durable no-reroll key before any page
            // formatting or registration work can throw. A pre-commit fault leaves this acceptance for
            // later same-day evidence; a post-commit fault settles it as written so that evidence cannot
            // create a duplicate page in the still-loaded game.
            RememberAcceptedAmbientInteractionFrequencyKey(key);
            long registrationBefore = events.RegistrationVersion;
            try
            {
                string label = AmbientLabel(note);
                string defName = AmbientDefName(note);
                string text = BuildAmbientInteractionText(note);
                string instruction = AmbientInstruction(note);
                string gameContext = "group=" + GameContextValue.Sanitize(note.GroupKey)
                    + "; batch=ambient_day_note"
                    + "; events=" + note.eventCount
                    + "; day=" + note.dayIndex
                    + "; participants=" + GameContextValue.Sanitize(
                        string.Join(", ", note.participantNames.ToArray()))
                    + "; first_tick=" + note.firstTick
                    + "; last_tick=" + note.lastTick;

                DiaryEvent diaryEvent = AddSoloEventWithFrozenMood(
                    note.pawn,
                    null,
                    defName,
                    label,
                    text,
                    instruction,
                    gameContext,
                    note.moodSnapshot);
                if (diaryEvent == null)
                {
                    // Registration declined atomically. Preserve the accepted candidate so later
                    // same-day evidence can retry the page without sampling frequency again.
                    return;
                }

                ForgetAcceptedAmbientInteractionFrequencyKey(key);
                writtenAmbientInteractionNotes.Add(key);
                AddPlayLogEntryIds(diaryEvent, note.playLogEntryIds);
                QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
            }
            catch
            {
                if (events.RegistrationVersion > registrationBefore)
                {
                    // Match dispatch's established commit boundary. The page may not have returned to
                    // this adapter, but persistence began, so reopening would risk a second page.
                    ForgetAcceptedAmbientInteractionFrequencyKey(key);
                    writtenAmbientInteractionNotes.Add(key);
                }

                throw;
            }
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
        private void FlushInteractionBatch(
            string key,
            PendingInteractionBatch batch,
            string excludedWriterPawnId = null)
        {
            pendingInteractionBatches.Remove(key);

            if (batch == null || batch.initiator == null || batch.recipient == null || batch.Count == 0)
            {
                return;
            }

            // The pending page already paid its one admission decision when the batch opened. Every
            // output shape below (standalone, solo survivor, or combined pair) honors that same result.
            if (!batch.frequencyAdmissionAccepted)
            {
                return;
            }

            // Freeze prose/mood at capture, but decide final ownership at flush: either participant may
            // have died while the batch waited, in which case the living side can still receive a solo.
            // Brainwipe also excludes only the wiped writer here. The other pawn's pre-wipe experience
            // remains theirs, while no delayed batch can recreate a page for the pawn who forgot it.
            bool initiatorExcluded = !string.IsNullOrWhiteSpace(excludedWriterPawnId)
                && string.Equals(
                    batch.initiatorPawnId,
                    excludedWriterPawnId,
                    StringComparison.Ordinal);
            bool recipientExcluded = !string.IsNullOrWhiteSpace(excludedWriterPawnId)
                && string.Equals(
                    batch.recipientPawnId,
                    excludedWriterPawnId,
                    StringComparison.Ordinal);
            bool initiatorEligible = !initiatorExcluded
                && !batch.initiator.Dead
                && IsDiaryEligible(batch.initiator);
            bool recipientEligible = !recipientExcluded
                && !batch.recipient.Dead
                && IsDiaryEligible(batch.recipient);

            if (!initiatorEligible && !recipientEligible)
            {
                return;
            }

            if (batch.Count == 1)
            {
                FlushStandaloneInteractionBatch(batch, initiatorEligible, recipientEligible);
                return;
            }

            bool combined = batch.Count > 1;
            string label = combined ? BatchLabel(batch) : batch.firstLabel;
            string defName = combined ? BatchDefName(batch) : batch.firstDefName;
            string initiatorText = BuildInteractionBatchText(batch, batch.initiatorLines);
            string recipientText = BuildInteractionBatchText(batch, batch.recipientLines);
            string instruction = BatchInstruction(batch);
            string gameContext = "group=" + GameContextValue.Sanitize(batch.GroupKey)
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

                MoodSnapshotCandidate eligibleMood = initiatorEligible
                    ? batch.initiatorMoodSnapshot
                    : batch.recipientMoodSnapshot;
                DiaryEvent soloEvent = AddSoloEventWithFrozenMood(
                    eligiblePawn,
                    otherPawn,
                    defName,
                    label,
                    eligibleText,
                    instruction,
                    gameContext,
                    eligibleMood);
                AddPlayLogEntryIds(soloEvent, batch.playLogEntryIds);
                QueueLlmRewrite(soloEvent, DiaryEvent.InitiatorRole);
                return;
            }

            DiaryEvent diaryEvent = AddPairwiseEventWithFrozenMood(
                batch.initiator,
                batch.recipient,
                defName,
                label,
                initiatorText,
                recipientText,
                instruction,
                gameContext,
                batch.initiatorMoodSnapshot,
                batch.recipientMoodSnapshot);
            // defName above may be a synthetic combined-batch name; keep the originating interaction's
            // real def so the generated-speech Social-log row can resolve a valid InteractionDef.
            diaryEvent.playLogInteractionDefName = batch.firstDefName;
            AddPlayLogEntryIds(diaryEvent, batch.playLogEntryIds);
            QueuePairwiseGeneration(diaryEvent);
        }

        /// <summary>
        /// A delayed batch that collected only one interaction is not really a batch. Emit it like the
        /// normal interaction path so the prompt does not receive combined-entry instructions.
        /// </summary>
        private void FlushStandaloneInteractionBatch(PendingInteractionBatch batch, bool initiatorEligible,
            bool recipientEligible)
        {
            string label = batch.firstLabel;
            string defName = batch.firstDefName;
            string instruction = DiaryLineCleaner.CleanLine(batch.instruction);
            string initiatorText = FirstStandaloneInteractionText(batch, true);
            string recipientText = FirstStandaloneInteractionText(batch, false);
            string gameContext = StandaloneInteractionBatchContext(batch);

            if (!initiatorEligible || !recipientEligible)
            {
                Pawn eligiblePawn = initiatorEligible ? batch.initiator : batch.recipient;
                Pawn otherPawn = initiatorEligible ? batch.recipient : batch.initiator;
                string eligibleText = initiatorEligible ? initiatorText : recipientText;
                if (string.IsNullOrWhiteSpace(eligibleText))
                {
                    eligibleText = "PawnDiary.Event.Interaction"
                        .Translate(eligiblePawn.LabelShortCap, label, otherPawn.LabelShortCap);
                }

                DiaryEvent soloEvent = AddSoloEvent(eligiblePawn, otherPawn, defName, label,
                    eligibleText, instruction, gameContext);
                AddPlayLogEntryIds(soloEvent, batch.playLogEntryIds);
                QueueLlmRewrite(soloEvent, DiaryEvent.InitiatorRole);
                return;
            }

            if (string.IsNullOrWhiteSpace(initiatorText))
            {
                initiatorText = "PawnDiary.Event.Interaction"
                    .Translate(batch.initiator.LabelShortCap, label, batch.recipient.LabelShortCap);
            }

            if (string.IsNullOrWhiteSpace(recipientText))
            {
                recipientText = initiatorText;
            }

            DiaryEvent diaryEvent = AddPairwiseEvent(batch.initiator, batch.recipient, defName, label,
                initiatorText, recipientText, instruction, gameContext);
            diaryEvent.playLogInteractionDefName = defName;
            AddPlayLogEntryIds(diaryEvent, batch.playLogEntryIds);
            QueuePairwiseGeneration(diaryEvent);
        }

        /// <summary>
        /// Captures each incoming moment's two detached mood candidates and retains the most extreme
        /// candidate per original batch POV, even when a later PlayLog row reverses pawn order.
        /// </summary>
        private static void RetainInteractionBatchMood(
            PendingInteractionBatch batch,
            Pawn eventInitiator,
            Pawn eventRecipient,
            int eventTick)
        {
            if (batch == null || eventInitiator == null || eventRecipient == null)
            {
                return;
            }

            MoodSnapshotCandidate initiatorMood =
                DiaryContextBuilder.CaptureMoodSnapshot(eventInitiator, eventTick);
            MoodSnapshotCandidate recipientMood =
                DiaryContextBuilder.CaptureMoodSnapshot(eventRecipient, eventTick);
            bool sameOrientation = string.Equals(
                eventInitiator.GetUniqueLoadID(),
                batch.initiatorPawnId,
                StringComparison.Ordinal);
            if (sameOrientation)
            {
                batch.initiatorMoodSnapshot = MoodSnapshotPolicy.PreferBatchSnapshot(
                    batch.initiatorMoodSnapshot,
                    initiatorMood);
                batch.recipientMoodSnapshot = MoodSnapshotPolicy.PreferBatchSnapshot(
                    batch.recipientMoodSnapshot,
                    recipientMood);
                return;
            }

            // The pair key is order-independent, so a later social-log row can arrive with the same
            // two pawns reversed. Keep candidates aligned to the batch's original POV slots.
            batch.initiatorMoodSnapshot = MoodSnapshotPolicy.PreferBatchSnapshot(
                batch.initiatorMoodSnapshot,
                recipientMood);
            batch.recipientMoodSnapshot = MoodSnapshotPolicy.PreferBatchSnapshot(
                batch.recipientMoodSnapshot,
                initiatorMood);
        }

        /// <summary>
        /// Remembers the first raw POV texts before the batch formatter turns them into accumulated lines.
        /// </summary>
        private static void CaptureFirstBatchTexts(PendingInteractionBatch batch, Pawn initiator,
            string initiatorText, string recipientText)
        {
            if (batch == null || initiator == null)
            {
                return;
            }

            if (initiator.GetUniqueLoadID() == batch.initiatorPawnId)
            {
                batch.firstInitiatorText = DiaryLineCleaner.CleanLine(initiatorText);
                batch.firstRecipientText = DiaryLineCleaner.CleanLine(recipientText);
            }
            else
            {
                batch.firstInitiatorText = DiaryLineCleaner.CleanLine(recipientText);
                batch.firstRecipientText = DiaryLineCleaner.CleanLine(initiatorText);
            }
        }

        private static string FirstStandaloneInteractionText(PendingInteractionBatch batch, bool initiatorPov)
        {
            string text = initiatorPov ? batch.firstInitiatorText : batch.firstRecipientText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            List<string> fallbackLines = initiatorPov ? batch.initiatorLines : batch.recipientLines;
            return BuildInteractionBatchText(batch, fallbackLines);
        }

        private static string StandaloneInteractionBatchContext(PendingInteractionBatch batch)
        {
            return "def=" + GameContextValue.Sanitize(DiaryLineCleaner.CleanLine(batch.firstDefName))
                + "; label=" + GameContextValue.Sanitize(DiaryLineCleaner.CleanLine(batch.firstLabel))
                + "; group=" + GameContextValue.Sanitize(batch.GroupKey)
                + "; events=1"
                + "; first_tick=" + batch.firstTick
                + "; last_tick=" + batch.lastTick;
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
        /// Admits one source row to an in-progress batch. A repeated non-negative PlayLog id is rejected;
        /// the negative sentinel used by older/non-PlayLog call paths remains independently admissible.
        /// </summary>
        private static bool TryAdmitPlayLogEntryId(List<int> playLogEntryIds, int playLogEntryId)
        {
            if (playLogEntryId < 0)
            {
                return true;
            }

            if (playLogEntryIds == null || playLogEntryIds.Contains(playLogEntryId))
            {
                return false;
            }

            playLogEntryIds.Add(playLogEntryId);
            return true;
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
                return TranslateBatchText(batch, batch.policy?.briefText, batch.policy?.briefKey,
                    "PawnDiary.Event.BatchBrief");
            }

            if (lines.Count == 1)
            {
                return lines[0];
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(TranslateBatchText(batch, batch.policy?.headerText, batch.policy?.headerKey,
                "PawnDiary.Event.BatchHeader"));
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
            builder.Append(TranslateAmbientText(note, note.policy?.headerText, note.policy?.headerKey,
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
            string batchingInstruction = TranslateBatchText(batch, batch.policy?.instructionText,
                batch.policy?.instructionKey,
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
            return TranslateBatchText(batch, batch.policy?.labelText, batch.policy?.labelKey,
                "PawnDiary.Event.BatchLabel");
        }

        /// <summary>
        /// Player-facing label for an ambient day note.
        /// </summary>
        private static string AmbientLabel(PendingAmbientInteractionNote note)
        {
            return TranslateAmbientText(note, note.policy?.labelText, note.policy?.labelKey,
                "PawnDiary.Event.AmbientDayLabel");
        }

        /// <summary>
        /// Natural-language fallback text for an ambient note with no usable sample lines.
        /// </summary>
        private static string AmbientFallback(PendingAmbientInteractionNote note)
        {
            if (!string.IsNullOrWhiteSpace(note.policy?.fallbackText))
            {
                return PromptTextTemplate.Format(note.policy.fallbackText,
                    note.pawn.LabelShortCap, AmbientGroupLabel(note));
            }

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
            if (!string.IsNullOrWhiteSpace(batch.policy?.fallbackText))
            {
                return PromptTextTemplate.Format(batch.policy.fallbackText,
                    eligiblePawn.LabelShortCap, otherPawn.LabelShortCap, BatchGroupLabel(batch));
            }

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
            string ambientInstruction = TranslateAmbientText(note, note.policy?.instructionText,
                note.policy?.instructionKey,
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
        private static string TranslateBatchText(PendingInteractionBatch batch, string policyText,
            string policyKey, string fallbackKey)
        {
            if (!string.IsNullOrWhiteSpace(policyText))
            {
                return PromptTextTemplate.Format(policyText, BatchGroupLabel(batch));
            }

            string key = string.IsNullOrWhiteSpace(policyKey) ? fallbackKey : policyKey;
            return key.Translate(BatchGroupLabel(batch)).Resolve();
        }

        /// <summary>
        /// Shared translator for ambient policy keys. Generic fallback keys receive the group label as {0}.
        /// </summary>
        private static string TranslateAmbientText(PendingAmbientInteractionNote note, string policyText,
            string policyKey, string fallbackKey)
        {
            if (!string.IsNullOrWhiteSpace(policyText))
            {
                return PromptTextTemplate.Format(policyText, AmbientGroupLabel(note));
            }

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
            return DailyEmissionGuardPolicy.InteractionKey(
                group.defName,
                pawn.GetUniqueLoadID(),
                dayIndex);
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
            // Exact group identity and the one aggregate admission result are frozen at open time.
            public string frequencyGroupKey;
            public string frequencyTier;
            public bool frequencyAdmissionAccepted = true;
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
            // Raw first-event POV text. Used if the batch flushes with one item, where it should become
            // an ordinary standalone interaction entry rather than a combined batch entry.
            public string firstInitiatorText;
            public string firstRecipientText;
            // Per-POV line accumulators — each moment appends one line per POV.
            public readonly List<string> initiatorLines = new List<string>();
            public readonly List<string> recipientLines = new List<string>();
            // B2 retains the most mood-extreme event-time candidate per original POV. These detached
            // values are sampled only after the final DiaryEvent receives its stable ID.
            public MoodSnapshotCandidate initiatorMoodSnapshot;
            public MoodSnapshotCandidate recipientMoodSnapshot;
            // RimWorld social-log ids represented by the eventual merged diary event.
            public readonly List<int> playLogEntryIds = new List<int>();

            public string GroupKey
            {
                get
                {
                    return string.IsNullOrWhiteSpace(frequencyGroupKey)
                        ? (group == null ? "unknown" : group.defName)
                        : frequencyGroupKey;
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
            // Exact group identity and the one pawn/day admission result are frozen at open time.
            public string frequencyGroupKey;
            public string frequencyTier;
            public bool frequencyAdmissionAccepted = true;
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
            // Most extreme mood observed while this per-pawn note accumulated; never re-read at flush.
            public MoodSnapshotCandidate moodSnapshot;

            public string GroupKey
            {
                get
                {
                    return string.IsNullOrWhiteSpace(frequencyGroupKey)
                        ? (group == null ? "unknown" : group.defName)
                        : frequencyGroupKey;
                }
            }
        }
    }
}

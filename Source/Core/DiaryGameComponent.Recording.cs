// The Record* hooks: the public entry points the Harmony patches (DiaryPatches.cs) call when the
// game produces something worth a diary entry — a social interaction, a mental break or social
// fight, a Tale (vanilla's notable-history events), a masterwork/legendary craft, an installed
// relic, a mood-affecting GameCondition, or a temporary thought. Each method classifies and
// dedups the event, builds its fallback text + game-context string, then hands off to the event
// factory (AddSoloEvent/AddPairwiseEvent) and the generation queue.
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
                if (RecentlyRecorded(recentMentalEvents, "fight|" + PairKey(pawn, otherPawn), DiaryTuning.Current.socialFightDedupTicks))
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

            if (RecentlyRecorded(recentMentalEvents, "break|" + pawn.GetUniqueLoadID() + "|" + stateDef.defName, DiaryTuning.Current.mentalBreakDedupTicks))
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
        /// Records one RimWorld TaleRecorder event. Tales are vanilla's broader notable-history
        /// events: deaths, wounds, surgeries, births, recruitment, research, disasters, and more.
        /// Some tales involve one pawn, others two; this method records a solo or pairwise diary
        /// event depending on how many eligible colonists are involved.
        /// </summary>
        public void RecordTale(Tale tale, TaleDef taleDef)
        {
            taleDef = taleDef ?? tale?.def;
            if (tale == null || taleDef == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (TaleDefsCoveredElsewhere.Contains(taleDef.defName) || !PawnDiaryMod.Settings.IsTaleEnabled(taleDef))
            {
                return;
            }

            // Several incidents (Eclipse, Aurora, ToxicFallout, VolcanicWinter, Flashstorm, ...) are
            // recorded BOTH as a Tale and as a GameCondition that shares the same defName. The
            // MoodEvent domain already captures the GameCondition with proper positive/negative mood
            // handling, so skip the Tale here to avoid logging the same event in the diary twice.
            if (DefDatabase<GameConditionDef>.GetNamedSilentFail(taleDef.defName) != null)
            {
                return;
            }

            Pawn firstPawn;
            Pawn secondPawn;
            ExtractTalePawns(tale, out firstPawn, out secondPawn);

            Pawn deathVictim = DeathVictimForTale(taleDef, firstPawn, secondPawn);
            bool deathDescription = IsDeathDescriptionEligible(deathVictim);

            bool firstEligible = IsDiaryEligible(firstPawn) || (deathDescription && firstPawn == deathVictim);
            bool secondEligible = IsDiaryEligible(secondPawn) || (deathDescription && secondPawn == deathVictim);
            if (!firstEligible && !secondEligible)
            {
                return;
            }

            string key = "tale|" + taleDef.defName + "|" + (firstPawn?.GetUniqueLoadID() ?? string.Empty)
                + "|" + (secondPawn?.GetUniqueLoadID() ?? string.Empty);
            if (RecentlyRecorded(recentTaleEvents, key, DiaryTuning.Current.taleDedupTicks))
            {
                return;
            }

            string label = CleanTaleLabel(taleDef);
            Def attachedDef = AttachedDefFor(tale);
            string instruction = PawnDiaryMod.Settings.InstructionForTale(taleDef);
            string gameContext = BuildTaleGameContext(tale, taleDef, label, attachedDef);
            if (deathDescription)
            {
                gameContext = AppendDeathDescriptionContext(gameContext, deathVictim, firstPawn, secondPawn);
            }

            if (firstEligible && secondEligible && firstPawn != secondPawn)
            {
                string text = BuildTalePairText(firstPawn, secondPawn, label, attachedDef);
                DiaryEvent pairEvent = AddPairwiseEvent(firstPawn, secondPawn, taleDef.defName, label,
                    text, text, instruction, gameContext);
                if (deathDescription)
                {
                    AddDeathEventRef(deathVictim, pairEvent.eventId);
                    QueueDeathDescription(pairEvent);
                    return;
                }

                QueuePairwiseGeneration(pairEvent);
                return;
            }

            Pawn povPawn = firstEligible ? firstPawn : secondPawn;
            Pawn otherPawn = firstEligible ? secondPawn : firstPawn;
            string soloText = BuildTaleSoloText(povPawn, label, otherPawn, attachedDef);
            DiaryEvent soloEvent = AddSoloEvent(povPawn, otherPawn, taleDef.defName, label, soloText, instruction, gameContext);
            if (deathDescription)
            {
                AddDeathEventRef(deathVictim, soloEvent.eventId);
                QueueDeathDescription(soloEvent);
                return;
            }

            QueueLlmRewrite(soloEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Records a masterwork or legendary item created by a colonist. Vanilla sends letters for
        /// these through QualityUtility rather than TaleRecorder, so this bridges that notification
        /// into the same solo diary-event flow used by Tale events.
        /// </summary>
        public void RecordCraftedQuality(Thing craftedThing, Pawn worker)
        {
            if (!IsDiaryEligible(worker) || craftedThing == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            QualityCategory quality;
            if (!QualityUtility.TryGetQuality(craftedThing, out quality)
                || (quality != QualityCategory.Masterwork && quality != QualityCategory.Legendary))
            {
                return;
            }

            // Art already flows through vanilla's CraftedArt tale (RecordTale), so recording the
            // quality letter for it too would produce two diary entries for one sculpture. Let the
            // tale own art; this hook covers only non-art masterwork/legendary items.
            if (craftedThing.TryGetComp<CompArt>() != null)
            {
                return;
            }

            if (!PawnDiaryMod.Settings.IsGroupEnabled(TaleQualityGroupKey))
            {
                return;
            }

            string defName = quality == QualityCategory.Legendary ? "CraftedLegendary" : "CraftedMasterwork";
            string key = "crafted_quality|" + worker.GetUniqueLoadID() + "|" + defName + "|" + craftedThing.ThingID;
            if (RecentlyRecorded(recentTaleEvents, key, DiaryTuning.Current.taleDedupTicks))
            {
                return;
            }

            string qualityLabel = ("QualityCategory_" + quality).Translate().Resolve();
            string itemLabel = DiaryContextBuilder.CleanLine(craftedThing.LabelShortCap);
            string label = "PawnDiary.Event.CraftedQualityLabel".Translate(qualityLabel).Resolve();
            string text = "PawnDiary.Event.CraftedQuality".Translate(worker.LabelShortCap, qualityLabel, itemLabel).Resolve();
            string instruction = PawnDiaryMod.Settings.InstructionForGroup(InteractionGroups.ByKey(TaleQualityGroupKey));
            string gameContext = "tale=" + defName
                + "; source=QualityUtility.SendCraftNotification"
                + "; quality=" + quality
                + "; item=" + itemLabel
                + "; itemDef=" + (craftedThing.def?.defName ?? string.Empty);

            DiaryEvent qualityEvent = AddSoloEvent(worker, null, defName, label, text, instruction, gameContext);
            QueueLlmRewrite(qualityEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Records the pawn who installs a venerated ideology relic in a reliquary. The relic
        /// container knows the item but not the worker, so the Harmony patch calls this from the
        /// install job's completion action where both are available.
        /// </summary>
        public void RecordRelicInstalled(Pawn pawn, Thing relic)
        {
            if (!IsDiaryEligible(pawn) || relic == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!PawnDiaryMod.Settings.IsGroupEnabled(TaleRelicGroupKey))
            {
                return;
            }

            string key = "relic_installed|" + pawn.GetUniqueLoadID() + "|" + relic.ThingID;
            if (RecentlyRecorded(recentTaleEvents, key, DiaryTuning.Current.taleDedupTicks))
            {
                return;
            }

            string relicLabel = DiaryContextBuilder.CleanLine(relic.LabelShortCap);
            string label = "PawnDiary.Event.RelicInstalledLabel".Translate().Resolve();
            string text = "PawnDiary.Event.RelicInstalled".Translate(pawn.LabelShortCap, relicLabel).Resolve();
            string instruction = PawnDiaryMod.Settings.InstructionForGroup(InteractionGroups.ByKey(TaleRelicGroupKey));
            string gameContext = "tale=RelicInstalled"
                + "; source=JobDriver_InstallRelic"
                + "; relic=" + relicLabel
                + "; relicDef=" + (relic.def?.defName ?? string.Empty);

            DiaryEvent relicEvent = AddSoloEvent(pawn, null, "RelicInstalled", label, text, instruction, gameContext);
            QueueLlmRewrite(relicEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Records a mood-affecting GameCondition (aurora, eclipse, psychic drone, toxic fallout,
        /// etc.) as solo diary events for each eligible colonist on affected maps. Called by the
        /// <see cref="GameConditionStartPatch"/> Harmony postfix on
        /// <c>GameConditionManager.RegisterCondition</c>.
        /// </summary>
        public void RecordMoodEvent(GameCondition condition)
        {
            if (condition == null || condition.def == null || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!PawnDiaryMod.Settings.IsMoodEventEnabled(condition.def))
            {
                return;
            }

            GameConditionDef conditionDef = condition.def;
            string conditionDefName = conditionDef.defName;
            string conditionLabel = DiaryContextBuilder.CleanLine(conditionDef.LabelCap.Resolve());

            // Dedup: the same GameCondition type within a short window avoids duplicate diary events
            // if a condition fires across multiple maps or re-triggers quickly.
            string dedupKey = "moodevent|" + conditionDefName;
            if (RecentlyRecorded(recentMoodEvents, dedupKey, DiaryTuning.Current.moodEventDedupTicks))
            {
                return;
            }

            string instruction = PawnDiaryMod.Settings.InstructionForMoodEvent(conditionDef);

            // The condition's own thought offset is identical for every colonist, so compute it once
            // here (it scans the whole ThoughtDef database) instead of inside the per-pawn loop.
            float conditionThoughtOffset = DiaryContextBuilder.GetMoodOffsetFromConditionThoughts(conditionDef);

            // Base gameContext shared by all pawns experiencing this condition.
            string baseContext = "mood_event=" + conditionDefName
                + "; label=" + conditionLabel;

            // Collect all eligible colonists on maps affected by this condition.
            // AffectedMaps can be empty for planet-wide conditions, in which case we
            // check all maps in play.
            List<Map> affectedMaps = condition.AffectedMaps;
            if (affectedMaps == null || affectedMaps.Count == 0)
            {
                affectedMaps = Find.Maps;
            }

            HashSet<string> recordedPawnIds = new HashSet<string>();
            for (int i = 0; i < affectedMaps.Count; i++)
            {
                Map map = affectedMaps[i];
                if (map == null || map.mapPawns == null)
                {
                    continue;
                }

                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int j = 0; j < colonists.Count; j++)
                {
                    Pawn pawn = colonists[j];
                    if (pawn == null || !IsDiaryEligible(pawn))
                    {
                        continue;
                    }

                    // A pawn could be on multiple maps during transitions; skip duplicates.
                    string pawnId = pawn.GetUniqueLoadID();
                    if (recordedPawnIds.Contains(pawnId))
                    {
                        continue;
                    }

                    recordedPawnIds.Add(pawnId);

                    // Each affected colonist gets their own solo diary event, since each
                    // pawn experiences the mood event independently. The mood impact direction
                    // (positive/negative/neutral) is determined per-pawn because some conditions
                    // (e.g. PsychicSuppressorMale) affect different sexes differently.
                    string moodImpact = DiaryContextBuilder.DetermineMoodImpact(condition, pawn, conditionThoughtOffset);
                    string perPawnContext = baseContext + "; mood_impact=" + moodImpact;

                    string text;
                    if (string.Equals(moodImpact, "positive", StringComparison.OrdinalIgnoreCase))
                    {
                        text = "PawnDiary.Event.MoodEventPositive".Translate(pawn.LabelShortCap, conditionLabel).Resolve();
                    }
                    else if (string.Equals(moodImpact, "negative", StringComparison.OrdinalIgnoreCase))
                    {
                        text = "PawnDiary.Event.MoodEventNegative".Translate(pawn.LabelShortCap, conditionLabel).Resolve();
                    }
                    else
                    {
                        text = "PawnDiary.Event.MoodEvent".Translate(pawn.LabelShortCap, conditionLabel).Resolve();
                    }

                    DiaryEvent moodEvent = AddSoloEvent(pawn, null, conditionDefName, conditionLabel,
                        text, instruction, perPawnContext);
                    moodEvent.moodImpact = moodImpact;
                    QueueLlmRewrite(moodEvent, DiaryEvent.InitiatorRole);
                }
            }
        }

        /// <summary>
        /// Records a diary event when a pawn gains a temporary thought with expiration.
        /// Filters out thoughts below magnitude thresholds (±5 general, ±15 eating),
        /// room stat thoughts, and deduplicates within the configured window.
        /// </summary>
        public void RecordThought(Pawn pawn, Thought_Memory thought)
        {
            if (pawn == null || thought == null || thought.def == null)
            {
                return;
            }

            if (!IsDiaryEligible(pawn))
            {
                return;
            }

            if (PawnDiaryMod.Settings == null || !PawnDiaryMod.Settings.IsThoughtEnabled(thought.def))
            {
                return;
            }

            if (thought.def.durationDays <= 0f)
            {
                return;
            }

            if (MatchesAnyToken(thought.def, DiaryTuning.Current.thoughtIgnoreTokens))
            {
                return;
            }

            float moodOffset = GetThoughtMoodOffset(thought);
            bool isEatingThought = MatchesAnyToken(thought.def, DiaryTuning.Current.thoughtEatingTokens);
            bool bypassThreshold = MatchesAnyToken(thought.def, DiaryTuning.Current.thoughtBypassThresholdTokens);

            // Bypass thoughts (death, banishment, etc.) are always recorded regardless of magnitude.
            // Everything else must clear its threshold; eating thoughts use the higher bar.
            if (!bypassThreshold)
            {
                float minMoodOffset = isEatingThought
                    ? DiaryTuning.Current.thoughtEatingMinMoodOffset
                    : DiaryTuning.Current.thoughtMinMoodOffset;
                if (Mathf.Abs(moodOffset) < minMoodOffset)
                {
                    return;
                }
            }

            string dedupKey = "thought|" + pawn.GetUniqueLoadID() + "|" + thought.def.defName;
            if (RecentlyRecorded(recentThoughtEvents, dedupKey, DiaryTuning.Current.thoughtDedupTicks))
            {
                return;
            }

            string thoughtDefName = thought.def.defName;
            string label = DiaryContextBuilder.CleanLine(thought.def.LabelCap.Resolve());
            string instruction = PawnDiaryMod.Settings.InstructionForThought(thought.def);

            string moodImpact = moodOffset > 0.5f ? "positive" : moodOffset < -0.5f ? "negative" : "neutral";

            string gameContext = "thought=" + thoughtDefName
                + "; label=" + label
                + "; mood_impact=" + moodImpact
                + "; mood_offset=" + moodOffset.ToString("F1")
                + "; duration_days=" + thought.def.durationDays.ToString("F1");

            string text;
            if (string.Equals(moodImpact, "positive", StringComparison.OrdinalIgnoreCase))
            {
                text = "PawnDiary.Event.ThoughtPositive".Translate(pawn.LabelShortCap, label).Resolve();
            }
            else if (string.Equals(moodImpact, "negative", StringComparison.OrdinalIgnoreCase))
            {
                text = "PawnDiary.Event.ThoughtNegative".Translate(pawn.LabelShortCap, label).Resolve();
            }
            else
            {
                text = "PawnDiary.Event.Thought".Translate(pawn.LabelShortCap, label).Resolve();
            }

            DiaryEvent thoughtEvent = AddSoloEvent(pawn, null, thoughtDefName, label, text, instruction, gameContext);
            thoughtEvent.moodImpact = moodImpact;
            QueueLlmRewrite(thoughtEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Returns true if the thought's defName contains any of the tokens in the given list (case-insensitive).
        /// Used for configurable ignore/bypass/classification token lists from DiaryTuningDef.
        /// </summary>
        private static bool MatchesAnyToken(ThoughtDef thoughtDef, List<string> tokens)
        {
            if (thoughtDef == null || string.IsNullOrEmpty(thoughtDef.defName) || tokens == null)
            {
                return false;
            }

            string defName = thoughtDef.defName;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (!string.IsNullOrEmpty(tokens[i])
                    && defName.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the mood offset for a thought's current stage. Uses Thought.MoodOffset()
        /// which returns the active stage's baseMoodEffect (not a sum of all stages).
        /// </summary>
        private static float GetThoughtMoodOffset(Thought_Memory thought)
        {
            if (thought == null)
            {
                return 0f;
            }

            return thought.MoodOffset();
        }
    }
}

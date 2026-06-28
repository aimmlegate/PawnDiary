// Tale batching — delayed solo diary entries for bursty TaleRecorder events. Combat can emit many
// Wounded/Downed/Killed tales in a few in-game hours; writing one diary page per pawn after the fight
// quiets down keeps the diary readable and protects the LLM queue. Death-description tales stay on
// the immediate path in DiaryGameComponent.Tales.cs because they need precise final chronology.
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
        /// Returns the classified Tale group when that group has an enabled per-pawn batch policy.
        /// </summary>
        internal static DiaryInteractionGroupDef TaleBatchGroupFor(TaleDef taleDef)
        {
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyTale(taleDef);
            return group != null && group.HasTaleBatchPolicy ? group : null;
        }

        /// <summary>
        /// Adds one Tale to each eligible pawn's pending solo batch instead of creating an immediate
        /// event. Each pawn gets their own final first-person diary entry when the batch flushes.
        /// </summary>
        internal void RecordBatchedTale(DiaryInteractionGroupDef group, Pawn firstPawn, Pawn secondPawn,
            bool firstEligible, bool secondEligible, TaleDef taleDef, string taleLabel, Def attachedDef,
            string instruction)
        {
            if (group == null || group.batch == null || taleDef == null)
            {
                return;
            }

            if (firstEligible && firstPawn != null)
            {
                AppendTaleBatch(group, firstPawn, secondPawn, taleDef, taleLabel, attachedDef, instruction);
            }

            if (secondEligible && secondPawn != null && secondPawn != firstPawn)
            {
                AppendTaleBatch(group, secondPawn, firstPawn, taleDef, taleLabel, attachedDef, instruction);
            }
        }

        /// <summary>
        /// Opens or appends to the pending per-pawn Tale batch for this group and source def.
        /// </summary>
        private void AppendTaleBatch(DiaryInteractionGroupDef group, Pawn pawn, Pawn otherPawn,
            TaleDef taleDef, string taleLabel, Def attachedDef, string instruction)
        {
            string key = TaleBatchKey(group, taleDef, pawn);
            PendingTaleBatch batch;
            if (!pendingTaleBatches.TryGetValue(key, out batch))
            {
                int now = Find.TickManager.TicksGame;
                batch = new PendingTaleBatch
                {
                    group = group,
                    policy = group.batch,
                    pawn = pawn,
                    pawnId = pawn.GetUniqueLoadID(),
                    firstTick = now,
                    lastTick = now,
                    firstDefName = taleDef.defName,
                    firstLabel = taleLabel,
                    instruction = instruction
                };
                pendingTaleBatches[key] = batch;
            }

            batch.eventCount++;
            batch.lastTick = Find.TickManager.TicksGame;
            AddTaleBatchSource(batch, taleDef);
            AddTaleBatchParticipant(batch, otherPawn);

            if (batch.sampleLines.Count < AmbientMaxSampleLines(batch.policy))
            {
                string text = BuildTaleSoloText(pawn, taleLabel, otherPawn, attachedDef);
                batch.sampleLines.Add(TaleBatchLine(batch, taleLabel, text));
            }

            if (batch.eventCount >= BatchMaxEvents(batch.policy))
            {
                FlushTaleBatch(key, batch);
            }
        }

        /// <summary>
        /// Builds the raw game text for a single-pawn tale, localized because it is shown in the Diary
        /// tab and also passed to the model as "what happened". Shared by the immediate Tale path
        /// (TaleSignal) and this batch flush. internal so the signal in PawnDiary.Ingestion can reuse it.
        /// Moved from the old DiaryGameComponent.Tales.cs.
        /// </summary>
        internal static string BuildTaleSoloText(Pawn povPawn, string label, Pawn otherPawn, Def attachedDef)
        {
            string text = otherPawn != null
                ? "PawnDiary.Event.TaleSoloWithOther".Translate(povPawn.LabelShortCap, label, otherPawn.LabelShortCap).Resolve()
                : "PawnDiary.Event.TaleSolo".Translate(povPawn.LabelShortCap, label).Resolve();

            return AppendAttachedDefText(text, attachedDef);
        }

        /// <summary>
        /// Appends the TaleData_Def label when vanilla supplied one (research project, skill, damage
        /// type, crafted object kind). Shared by the Tale text builders. Moved from Tales.cs.
        /// </summary>
        internal static string AppendAttachedDefText(string text, Def attachedDef)
        {
            if (attachedDef == null)
            {
                return text;
            }

            string label = DiaryLineCleaner.CleanLine(attachedDef.LabelCap.Resolve());
            return string.IsNullOrWhiteSpace(label)
                ? text
                : text + "PawnDiary.Event.TaleAttachedDef".Translate(label).Resolve();
        }

        /// <summary>
        /// Called each tick: flushes Tale batches that exceeded their quiet window or max count.
        /// </summary>
        private void FlushReadyTaleBatches()
        {
            if (pendingTaleBatches.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            List<string> keysToFlush = new List<string>();
            foreach (KeyValuePair<string, PendingTaleBatch> pair in pendingTaleBatches)
            {
                PendingTaleBatch batch = pair.Value;
                if (batch == null
                    || batch.eventCount >= BatchMaxEvents(batch.policy)
                    || now - batch.lastTick >= BatchWindowTicks(batch.policy))
                {
                    keysToFlush.Add(pair.Key);
                }
            }

            FlushTaleBatches(keysToFlush);
        }

        /// <summary>
        /// Flushes every pending Tale batch immediately (used on save to avoid data loss).
        /// </summary>
        private void FlushAllTaleBatches()
        {
            if (pendingTaleBatches.Count == 0)
            {
                return;
            }

            FlushTaleBatches(new List<string>(pendingTaleBatches.Keys));
        }

        /// <summary>
        /// Flushes each Tale batch identified by its key.
        /// </summary>
        private void FlushTaleBatches(List<string> keysToFlush)
        {
            if (keysToFlush == null)
            {
                return;
            }

            for (int i = 0; i < keysToFlush.Count; i++)
            {
                PendingTaleBatch batch;
                if (pendingTaleBatches.TryGetValue(keysToFlush[i], out batch))
                {
                    FlushTaleBatch(keysToFlush[i], batch);
                }
            }
        }

        /// <summary>
        /// Converts a finished Tale batch into one solo DiaryEvent and queues normal generation.
        /// </summary>
        private void FlushTaleBatch(string key, PendingTaleBatch batch)
        {
            pendingTaleBatches.Remove(key);

            if (batch == null || batch.pawn == null || batch.eventCount == 0 || !IsDiaryEligible(batch.pawn))
            {
                return;
            }

            bool combined = batch.eventCount > 1;
            string label = combined ? TaleBatchLabel(batch) : batch.firstLabel;
            string defName = combined ? TaleBatchDefName(batch) : batch.firstDefName;
            string text = BuildTaleBatchText(batch);
            string instruction = TaleBatchInstruction(batch);
            string gameContext = BuildTaleBatchGameContext(batch, defName, label);

            DiaryEvent diaryEvent = AddSoloEvent(batch.pawn, null, defName, label, text, instruction, gameContext);
            QueueLlmRewrite(diaryEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Formats accumulated Tale evidence into one raw "what happened" string.
        /// </summary>
        private static string BuildTaleBatchText(PendingTaleBatch batch)
        {
            if (batch.sampleLines == null || batch.sampleLines.Count == 0)
            {
                return TaleBatchFallback(batch);
            }

            if (batch.sampleLines.Count == 1)
            {
                return batch.sampleLines[0];
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(TranslateTaleBatchKey(batch, batch.policy?.headerKey,
                "PawnDiary.Event.TaleBatchHeader"));
            for (int i = 0; i < batch.sampleLines.Count; i++)
            {
                builder.Append("\n").Append("- ").Append(batch.sampleLines[i]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Formats one Tale evidence line, optionally keeping the Tale label for clarity.
        /// </summary>
        private static string TaleBatchLine(PendingTaleBatch batch, string taleLabel, string text)
        {
            bool includeLabel = batch.policy == null || batch.policy.includeInteractionLabel;
            return InteractionBatchLine(includeLabel ? taleLabel : null, text);
        }

        /// <summary>
        /// Appends the batching instruction so the LLM writes one combined first-person page.
        /// </summary>
        private static string TaleBatchInstruction(PendingTaleBatch batch)
        {
            string instruction = DiaryLineCleaner.CleanLine(batch.instruction);
            string batchingInstruction = TranslateTaleBatchKey(batch, batch.policy?.instructionKey,
                "PawnDiary.Event.TaleBatchInstruction");
            if (string.IsNullOrWhiteSpace(instruction))
            {
                return batchingInstruction;
            }

            return instruction + "; " + batchingInstruction;
        }

        /// <summary>
        /// Player-facing label for a combined Tale batch.
        /// </summary>
        private static string TaleBatchLabel(PendingTaleBatch batch)
        {
            return TranslateTaleBatchKey(batch, batch.policy?.labelKey, "PawnDiary.Event.TaleBatchLabel");
        }

        /// <summary>
        /// Fallback game text when a Tale batch has no usable sample lines.
        /// </summary>
        private static string TaleBatchFallback(PendingTaleBatch batch)
        {
            string key = string.IsNullOrWhiteSpace(batch.policy?.fallbackKey)
                ? "PawnDiary.Event.TaleBatchFallback"
                : batch.policy.fallbackKey;
            return key.Translate(batch.pawn.LabelShortCap, TaleBatchGroupLabel(batch)).Resolve();
        }

        /// <summary>
        /// Synthetic defName for a combined Tale batch.
        /// </summary>
        private static string TaleBatchDefName(PendingTaleBatch batch)
        {
            if (batch.policy != null && !string.IsNullOrWhiteSpace(batch.policy.syntheticDefName))
            {
                return batch.policy.syntheticDefName;
            }

            return batch.GroupKey + "TaleBatch";
        }

        /// <summary>
        /// Shared translator for Tale batch policy keys. Generic fallback keys receive the group label.
        /// </summary>
        private static string TranslateTaleBatchKey(PendingTaleBatch batch, string policyKey, string fallbackKey)
        {
            string key = string.IsNullOrWhiteSpace(policyKey) ? fallbackKey : policyKey;
            return key.Translate(TaleBatchGroupLabel(batch)).Resolve();
        }

        /// <summary>
        /// Localized group label used in generic Tale batch text.
        /// </summary>
        private static string TaleBatchGroupLabel(PendingTaleBatch batch)
        {
            return batch.group == null ? string.Empty : batch.group.LabelCap.Resolve();
        }

        /// <summary>
        /// Compact metadata string for the delayed solo event. The leading tale= marker keeps the
        /// event in the Tale domain, and batch=tale lets prompt selection recognize delayed batches.
        /// </summary>
        private static string BuildTaleBatchGameContext(PendingTaleBatch batch, string defName, string label)
        {
            List<string> parts = new List<string>
            {
                "tale=" + defName,
                "label=" + DiaryLineCleaner.CleanLine(label),
                "taleClass=TaleBatch",
                "group=" + batch.GroupKey,
                "batch=tale",
                "events=" + batch.eventCount,
                "day=" + CurrentDayIndex,
                "first_tick=" + batch.firstTick,
                "last_tick=" + batch.lastTick
            };

            if (batch.taleDefNames.Count > 0)
            {
                parts.Add("tale_defs=" + string.Join(", ", batch.taleDefNames.ToArray()));
            }

            if (batch.participantNames.Count > 0)
            {
                parts.Add("participants=" + string.Join(", ", batch.participantNames.ToArray()));
            }

            return string.Join("; ", parts.ToArray());
        }

        /// <summary>
        /// Produces a deterministic key for the configured Tale batch scope.
        /// </summary>
        private static string TaleBatchKey(DiaryInteractionGroupDef group, TaleDef taleDef, Pawn pawn)
        {
            string key = group.defName;
            if (group.batch != null && group.batch.scope == InteractionBatchScope.Def)
            {
                key += "|" + taleDef.defName;
            }

            return key + "|tale|" + pawn.GetUniqueLoadID();
        }

        /// <summary>
        /// Remembers each source TaleDef once for debug context.
        /// </summary>
        private static void AddTaleBatchSource(PendingTaleBatch batch, TaleDef taleDef)
        {
            string defName = taleDef?.defName;
            if (string.IsNullOrWhiteSpace(defName) || batch.taleDefNames.Contains(defName))
            {
                return;
            }

            batch.taleDefNames.Add(defName);
        }

        /// <summary>
        /// Adds the other pawn once, when the Tale had another live participant.
        /// </summary>
        private static void AddTaleBatchParticipant(PendingTaleBatch batch, Pawn otherPawn)
        {
            if (batch == null || otherPawn == null)
            {
                return;
            }

            string id = otherPawn.GetUniqueLoadID();
            if (batch.participantIds.Contains(id))
            {
                return;
            }

            batch.participantIds.Add(id);
            batch.participantNames.Add(DiaryLineCleaner.CleanLine(otherPawn.LabelShortCap));
        }

        /// <summary>
        /// Accumulates Tale evidence for one pawn until the configured quiet window expires.
        /// </summary>
        private class PendingTaleBatch
        {
            public DiaryInteractionGroupDef group;
            public InteractionBatchPolicy policy;
            // Live Pawn reference — only valid during the current game session (not saved).
            public Pawn pawn;
            public string pawnId;
            public int firstTick;
            public int lastTick;
            public string firstDefName;
            public string firstLabel;
            public string instruction;
            public int eventCount;
            public readonly List<string> sampleLines = new List<string>();
            public readonly List<string> taleDefNames = new List<string>();
            public readonly List<string> participantIds = new List<string>();
            public readonly List<string> participantNames = new List<string>();

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

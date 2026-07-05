// Quest ingestion signal — the impure capture+emit half of the "quest lifecycle" source (accepted /
// completed / failed). Replaces the shared DiaryGameComponent.RecordQuestSignal fan-out core. Quests
// are colony-wide: one solo entry per eligible colonist, with a single quest+signal dedup window.
//
// The thin RecordQuestAccepted/RecordQuestEnded methods stay on the component (they update the
// accept-scanner baseline and map outcomes), and submit this fan-out. The quest lifecycle ALSO emits a
// generic event-window signal — that is separate XML policy, so it fires once at capture (before the
// diary group gate) regardless of whether quest diary entries are enabled. Pure decision +
// game-context + display-label cleanup live in Source/Capture/Events/QuestEventData.cs.
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using System.Text;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Colony-wide quest fan-out. Built by <see cref="DiaryGameComponent.RecordQuestAccepted"/> /
    /// <see cref="DiaryGameComponent.RecordQuestEnded"/> and submitted via
    /// <see cref="DiaryEvents.Submit(DiaryFanoutSignal)"/>.
    /// </summary>
    internal sealed class QuestFanoutSignal : DiaryFanoutSignal
    {
        // Defensive caps for the prose fields embedded in the prompt (stable parser/length caps, not
        // tunable feature policy — AGENTS.md section 12). Moved from DiaryGameComponent.Quests.cs.
        private const int QuestDescriptionCap = 600;
        private const int QuestRewardsCap = 300;

        private readonly bool valid;
        private readonly string colonyDedupKey;

        internal string Signal { get; }
        internal string QuestDefName { get; }
        internal string CleanedLabel { get; }
        internal string FactionDefName { get; }
        internal string Rewards { get; }
        internal string Description { get; }
        internal string Instruction { get; }
        internal string TextKey { get; }

        public QuestFanoutSignal(Quest quest, string signal, string textKey)
        {
            if (!DiaryGameComponent.GamePlaying || quest == null || string.IsNullOrEmpty(signal) || PawnDiaryMod.Settings == null)
            {
                return;
            }

            Signal = signal;
            TextKey = textKey;
            QuestDefName = string.IsNullOrEmpty(quest.root?.defName) ? "unknown" : quest.root.defName;
            CleanedLabel = BuildQuestLabel(quest);

            // Event windows are generic XML policy, not the Quest-domain diary group. Emit the lifecycle
            // signal before group gating so XML can watch a quest without forcing all quest diary
            // entries on. Isolate it so an event-window failure cannot skip the quest capture.
            try
            {
                DiaryGameComponent.Instance?.RecordEventWindowSignal(
                    DiaryGameComponent.EventWindowSourceQuest, QuestDefName, signal, CleanedLabel, null);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Quest event-window signal failed and was skipped: " + e,
                    "QuestFanoutSignal.EventWindow".GetHashCode());
            }

            // XML owns which lifecycle signals count as diary-worthy. The signal IS the classifier key.
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyQuest(signal);
            if (group == null || !PawnDiaryMod.Settings.IsQuestEnabled(signal))
            {
                return;
            }

            FactionDefName = BuildQuestFactionDefName(quest);
            Rewards = BuildQuestRewardsSummary(quest);

            // Resolve the description once and reject malformed quests (blank or RimWorld's generated
            // "ERR:" placeholder text) so they never produce a diary page. The event-window signal
            // above still fired, but no per-pawn diary entries are generated for these quests.
            Description = BuildQuestDescription(quest);
            if (string.IsNullOrEmpty(Description))
            {
                return;
            }

            Instruction = InteractionGroups.InstructionForQuest(signal);
            colonyDedupKey = "quest|" + quest.id + "|" + signal;
            valid = true;
        }

        public override string ColonyDedupKey => valid ? colonyDedupKey : string.Empty;

        public override int ColonyDedupTicks => DiaryTuning.Current.questDedupTicks;

        public override IEnumerable<DiarySignal> PerPawnSignals()
        {
            if (!valid)
            {
                yield break;
            }

            HashSet<string> seen = new HashSet<string>();
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map?.mapPawns == null)
                {
                    continue;
                }

                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int j = 0; j < colonists.Count; j++)
                {
                    Pawn pawn = colonists[j];
                    if (pawn == null || !DiaryGameComponent.IsDiaryEligible(pawn))
                    {
                        continue;
                    }

                    string pawnId = pawn.GetUniqueLoadID();
                    if (!seen.Add(pawnId))
                    {
                        continue;
                    }

                    yield return new QuestPawnSignal(this, pawn, pawnId);
                }
            }
        }

        // ── Helpers moved verbatim from the old DiaryGameComponent.Quests.cs ──

        private static string BuildQuestLabel(Quest quest)
        {
            return QuestEventData.BuildDisplayLabel(quest.name, quest.root?.label, quest.root?.defName);
        }

        private static string BuildQuestFactionDefName(Quest quest)
        {
            try
            {
                foreach (Faction faction in quest.InvolvedFactions)
                {
                    if (faction == null || faction == Faction.OfPlayer)
                    {
                        continue;
                    }

                    string defName = faction.def?.defName;
                    if (!string.IsNullOrEmpty(defName))
                    {
                        return defName;
                    }
                }
            }
            catch
            {
                // InvolvedFactions enumeration is vanilla-side and should not throw, but the faction
                // field is best-effort context — never fail the diary entry over it.
            }

            return QuestEventData.FactionUnknown;
        }

        private static string BuildQuestRewardsSummary(Quest quest)
        {
            try
            {
                List<QuestPart> parts = quest.PartsListForReading;
                if (parts == null || parts.Count == 0)
                {
                    return QuestEventData.RewardsNone;
                }

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < parts.Count; i++)
                {
                    QuestPart_DropPods dropPods = parts[i] as QuestPart_DropPods;
                    if (dropPods?.thingDefs == null || dropPods.thingDefs.Count == 0)
                    {
                        continue;
                    }

                    for (int j = 0; j < dropPods.thingDefs.Count; j++)
                    {
                        ThingDefCountClass reward = dropPods.thingDefs[j];
                        ThingDef thingDef = reward?.thingDef;
                        if (thingDef == null)
                        {
                            continue;
                        }

                        string thingLabel = !string.IsNullOrWhiteSpace(thingDef.label)
                            ? thingDef.label
                            : thingDef.defName;

                        if (sb.Length > 0)
                        {
                            sb.Append(", ");
                        }

                        sb.Append(thingLabel).Append(" x").Append(reward.count);

                        if (sb.Length > QuestRewardsCap)
                        {
                            break;
                        }
                    }

                    if (sb.Length > QuestRewardsCap)
                    {
                        break;
                    }
                }

                if (sb.Length == 0)
                {
                    return QuestEventData.RewardsNone;
                }

                if (sb.Length > QuestRewardsCap)
                {
                    return sb.ToString(0, QuestRewardsCap) + "...";
                }

                return sb.ToString();
            }
            catch
            {
                return QuestEventData.RewardsNone;
            }
        }

        private static string BuildQuestDescription(Quest quest)
        {
            try
            {
                string description = quest.description.Resolve();
                // Reject blank and RimWorld's generated/placeholder "ERR:" descriptions before they
                // reach a diary prompt. The QuestFanoutSignal constructor drops quests whose
                // description resolves to empty here.
                if (QuestEventData.IsMalformedResolvedQuestDescription(description))
                {
                    return string.Empty;
                }

                description = PromptTextSanitizer.LocalizedPromptText(description);
                if (description.Length > QuestDescriptionCap)
                {
                    return TextTruncation.SafePrefix(description, QuestDescriptionCap) + "...";
                }

                return description;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    /// <summary>One eligible colonist's solo quest entry.</summary>
    internal sealed class QuestPawnSignal : DiarySignal
    {
        private readonly QuestFanoutSignal source;
        private readonly Pawn pawn;
        private readonly QuestEventData payload;

        public QuestPawnSignal(QuestFanoutSignal source, Pawn pawn, string pawnId)
        {
            this.source = source;
            this.pawn = pawn;
            payload = new QuestEventData
            {
                PawnId = pawnId,
                Tick = Find.TickManager.TicksGame,
                Signal = source.Signal,
                DefName = source.QuestDefName,
                Label = source.CleanedLabel,
                FactionDefName = source.FactionDefName,
                Rewards = source.Rewards,
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: true, userEnabled: true, signalEnabled: true, ambientSignalEnabled: true);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            string perPawnContext = QuestEventData.BuildGameContext(
                source.QuestDefName, source.Signal, source.CleanedLabel, source.FactionDefName, source.Rewards);

            // The description is prose, so it lives in the localized event text (fed to the LLM as
            // "what happened") rather than the structured game-context marker.
            string text = source.TextKey.Translate(pawn.LabelShortCap, source.CleanedLabel, source.Description).Resolve();

            DiaryEvent questEvent = sink.AddSoloEvent(pawn, null, source.QuestDefName, source.CleanedLabel,
                text, source.Instruction, perPawnContext);
            if (questEvent == null)
            {
                return;
            }

            sink.QueueSolo(questEvent, DiaryEvent.InitiatorRole);
        }
    }
}

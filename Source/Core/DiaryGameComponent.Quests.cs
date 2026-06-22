// Quest events — the Quest.Accept and Quest.End hooks' diary flow, built on the Event Catalog
// pattern (see Source/Capture/). Quests are colony-wide: when the player accepts a quest, and
// again when the quest ends in Success or Fail, every eligible colonist gets their own solo
// DiaryEvent so each survivor can react in their own voice. The event carries a "quest=" marker
// so the UI classifies it into the Quest domain.
//
// Rich context (per the brief): quest defName, the quest's description prose, the issuer faction
// defName, and a short rewards summary. The description lives in the localized event text (it is
// prose); the faction + rewards live in the structured game-context marker so prompt policy can
// read them as fields. Only ACCEPTED quests are recorded — offered-but-not-accepted quests never
// reach RecordQuestAccepted, and RecordQuestEnded maps Success -> "completed" / Fail -> "failed".
//
// This file holds both halves of the multi-pawn fan-out (mirroring MoodEvents.cs):
// (1) per-source-call gates that stay impure and outside the catalog — CanRecordGameplayEventNow,
//     the user-toggle gate, the per-signal dedup (peek + conditional mark);
// (2) the per-pawn catalog dispatch loop — for each eligible colonist on every map, build a
//     QuestEventData + CaptureContext, ask the catalog for a decision, and if GenerateSolo build
//     the entry via the pure QuestEventData.BuildGameContext helper and the localized event text.
//
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // Defensive caps for the prose fields embedded in the prompt. Per AGENTS.md section 12 these
        // stay hardcoded C#: they are stable parser/length caps, not tunable feature policy.
        private const int QuestDescriptionCap = 600;
        private const int QuestRewardsCap = 300;

        /// <summary>
        /// Records a freshly accepted quest as solo diary events for every eligible colonist. Called
        /// by the <see cref="QuestAcceptPatch"/> Harmony postfix on <c>Quest.Accept</c>. Only accepted
        /// quests reach this method — offered quests from QuestManager.Add are ignored, per the
        /// requirement "only quest that accepted".
        /// </summary>
        public void RecordQuestAccepted(Quest quest)
        {
            RecordQuestSignal(quest, QuestEventData.SignalAccepted, "PawnDiary.Event.QuestAccepted");
        }

        /// <summary>
        /// Records a quest ending as solo diary events for every eligible colonist. Called by the
        /// <see cref="QuestEndPatch"/> Harmony postfix on <c>Quest.End</c>. Only Success and Fail
        /// outcomes are forwarded (the patch filters); Success -> "completed", Fail -> "failed".
        /// </summary>
        public void RecordQuestEnded(Quest quest, QuestEndOutcome outcome)
        {
            string signal = QuestSignalForOutcome(outcome);
            if (string.IsNullOrEmpty(signal))
            {
                return;
            }

            string textKey = signal == QuestEventData.SignalCompleted
                ? "PawnDiary.Event.QuestCompleted"
                : "PawnDiary.Event.QuestFailed";
            RecordQuestSignal(quest, signal, textKey);
        }

        /// <summary>
        /// Shared fan-out for all three quest signals. Snapshots the quest's rich context once,
        /// dedups per quest+signal, then dispatches one pure Decide per eligible colonist.
        /// </summary>
        private void RecordQuestSignal(Quest quest, string signal, string textKey)
        {
            if (!CanRecordGameplayEventNow()
                || quest == null
                || string.IsNullOrEmpty(signal)
                || PawnDiaryMod.Settings == null)
            {
                return;
            }

            // XML owns which lifecycle signals count as diary-worthy. The signal IS the classifier
            // key: "accepted" -> questAccepted, "completed" -> questCompleted, "failed" -> questFailed.
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyQuest(signal);
            if (group == null || !PawnDiaryMod.Settings.IsQuestEnabled(signal))
            {
                return;
            }

            // Snapshot the quest's rich context once (these scan parts / involved factions).
            string questDefName = quest.root?.defName;
            if (string.IsNullOrEmpty(questDefName))
            {
                questDefName = "unknown";
            }

            string cleanedLabel = BuildQuestLabel(quest);
            string factionDefName = BuildQuestFactionDefName(quest);
            string rewards = BuildQuestRewardsSummary(quest);
            string description = BuildQuestDescription(quest);

            // Per-signal dedup so a multi-map transition or a fluke double-fire cannot double-record.
            string dedupKey = "quest|" + quest.id + "|" + signal;
            if (IsRecentlyRecorded(recentQuestEvents, dedupKey, DiaryTuning.Current.questDedupTicks))
            {
                return;
            }

            string instruction = PawnDiaryMod.Settings.InstructionForQuest(signal);
            DiaryEventSpec spec = DiaryEventCatalog.Get(DiaryEventType.Quest);

            // Quests are colony-wide: fan out to every eligible colonist on every map. A pawn on two
            // maps during a transition is deduped by id within this loop.
            HashSet<string> recordedPawnIds = new HashSet<string>();
            bool recordedAny = false;
            List<Map> maps = Find.Maps;
            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                Map map = maps[mapIndex];
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

                    string pawnId = pawn.GetUniqueLoadID();
                    if (recordedPawnIds.Contains(pawnId))
                    {
                        continue;
                    }
                    recordedPawnIds.Add(pawnId);

                    QuestEventData data = new QuestEventData
                    {
                        PawnId = pawnId,
                        Tick = Find.TickManager.TicksGame,
                        Signal = signal,
                        DefName = questDefName,
                        Label = cleanedLabel,
                        FactionDefName = factionDefName,
                        Rewards = rewards,
                    };
                    CaptureContext ctx = BuildCaptureContext(
                        eligible: true,
                        userEnabled: true,
                        signalEnabled: true,
                        ambientSignalEnabled: true);

                    CaptureDecision decision = spec != null
                        ? spec.Decide(data, ctx)
                        : CaptureDecision.Drop;
                    if (decision != CaptureDecision.GenerateSolo)
                    {
                        continue;
                    }

                    string perPawnContext = QuestEventData.BuildGameContext(
                        questDefName, signal, cleanedLabel, factionDefName, rewards);

                    // The description is prose, so it lives in the localized event text (fed to the
                    // LLM as "what happened") rather than the structured game-context marker.
                    string text = textKey.Translate(pawn.LabelShortCap, cleanedLabel, description).Resolve();

                    DiaryEvent questEvent = AddSoloEvent(pawn, null, questDefName, cleanedLabel,
                        text, instruction, perPawnContext);
                    if (questEvent == null)
                    {
                        continue;
                    }

                    recordedAny = true;
                    QueueLlmRewrite(questEvent, DiaryEvent.InitiatorRole);
                }
            }

            if (recordedAny)
            {
                MarkRecentlyRecorded(recentQuestEvents, dedupKey, DiaryTuning.Current.questDedupTicks);
            }
        }

        /// <summary>
        /// Maps a RimWorld quest outcome to our diary signal. Success -> "completed", Fail ->
        /// "failed"; any other outcome (Unknown / InvalidPreAcceptance) returns null and is skipped.
        /// </summary>
        private static string QuestSignalForOutcome(QuestEndOutcome outcome)
        {
            if (outcome == QuestEndOutcome.Success)
            {
                return QuestEventData.SignalCompleted;
            }

            if (outcome == QuestEndOutcome.Fail)
            {
                return QuestEventData.SignalFailed;
            }

            return null;
        }

        /// <summary>
        /// Builds the human-readable quest label, preferring the generated quest name, then the
        /// script def's label, then the defName. Cleaned for prompt embedding.
        /// </summary>
        private static string BuildQuestLabel(Quest quest)
        {
            string label = quest.name;
            if (string.IsNullOrWhiteSpace(label))
            {
                label = quest.root?.label;
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                label = quest.root?.defName;
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                return "unknown";
            }

            return DiaryContextBuilder.CleanLine(label);
        }

        /// <summary>
        /// Extracts the issuer/requester faction defName from the quest's involved factions, picking
        /// the first non-player faction. Returns <c>"unknown"</c> when the quest exposes no faction
        /// (a factionless quest). English sentinel per AGENTS.md section 12.
        /// </summary>
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

        /// <summary>
        /// Builds a short, cleaned summary of the quest's item rewards by scanning the quest's parts
        /// for reward-bearing <see cref="QuestPart_DropPods"/> (whose <c>thingDefs</c> list is the
        /// canonical "what gets delivered" set). Format: "Silver x100, Medicine x5". Returns
        /// <c>"none"</c> when no rewards are found. Capped at <see cref="QuestRewardsCap"/> chars.
        /// </summary>
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

                        // Stop once we have plenty — the summary only steers prompt tone.
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
                // Reward scanning is best-effort context; never fail the diary entry over it.
                return QuestEventData.RewardsNone;
            }
        }

        /// <summary>
        /// Reads and cleans the quest's display description, capped at
        /// <see cref="QuestDescriptionCap"/> chars (with a trailing "..." when truncated). The
        /// description is prose, so it is returned for embedding in the localized event text rather
        /// than in the structured game-context marker.
        /// </summary>
        private static string BuildQuestDescription(Quest quest)
        {
            try
            {
                string description = quest.description.Resolve();
                if (string.IsNullOrWhiteSpace(description))
                {
                    return string.Empty;
                }

                description = DiaryContextBuilder.CleanLine(description);
                if (description.Length > QuestDescriptionCap)
                {
                    return description.Substring(0, QuestDescriptionCap) + "...";
                }

                return description;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}

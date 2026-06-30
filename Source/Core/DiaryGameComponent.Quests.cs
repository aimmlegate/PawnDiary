// Quest events — the accept/end hooks + the defensive accept-state scanner. The colony-wide fan-out
// (snapshot the quest's rich context, dedup per quest+signal, emit one entry per eligible colonist,
// plus the generic event-window lifecycle signal) moved to QuestFanoutSignal
// (Source/Ingestion/Sources/). What stays here is the thin orchestration that both the Harmony hooks
// and the scanner share: marking a quest as seen so the scanner does not double-record, mapping an
// end outcome to a signal, and submitting the fan-out. Accepted quests are bookkeeping/event-window
// signals only; RecordQuestEnded maps Success -> "completed" / Fail -> "failed" diary pages.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Marks a freshly accepted quest as seen and forwards the lifecycle signal to generic
        /// event-window policy. Accepted quests do not generate diary pages; completed/failed endings
        /// do.
        /// </summary>
        public void RecordQuestAccepted(Quest quest)
        {
            MarkAcceptedQuestSeen(quest);
            DiaryEvents.Submit(new QuestFanoutSignal(quest, QuestEventData.SignalAccepted, "PawnDiary.Event.QuestAccepted"));
        }

        /// <summary>
        /// Records a quest ending. Called by the <see cref="QuestEndPatch"/> Harmony postfix on
        /// <c>Quest.End</c>. Only Success and Fail outcomes are forwarded; Success -> "completed",
        /// Fail -> "failed".
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
            DiaryEvents.Submit(new QuestFanoutSignal(quest, signal, textKey));
        }

        /// <summary>
        /// Remembers quests that were already accepted when a save loaded. This keeps the acceptance
        /// scanner from backfilling old quests while still letting newly accepted quests in the same
        /// session be recorded.
        /// </summary>
        private bool BaselineAcceptedQuests()
        {
            QuestManager manager = Find.QuestManager;
            if (manager == null)
            {
                return false;
            }

            List<Quest> quests = manager.QuestsListForReading;
            if (quests == null)
            {
                return false;
            }

            knownAcceptedQuestIds.Clear();
            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];
                if (quest != null && quest.EverAccepted)
                {
                    knownAcceptedQuestIds.Add(quest.id);
                }
            }

            return true;
        }

        /// <summary>
        /// Periodically catches quest acceptances by observing the Quest.EverAccepted state. The direct
        /// Quest.Accept hook remains the immediate path; this scanner is the defensive fallback for
        /// RimWorld UI or modded flows that mark a quest accepted without hitting our patch.
        /// </summary>
        private void ScanAcceptedQuestsForDiaryEvents()
        {
            if (baselineQuestAcceptancesOnNextScan)
            {
                baselineQuestAcceptancesOnNextScan = !BaselineAcceptedQuests();
                return;
            }

            if (!CanRecordGameplayEventNow())
            {
                return;
            }

            QuestManager manager = Find.QuestManager;
            if (manager == null)
            {
                return;
            }

            List<Quest> quests = manager.QuestsListForReading;
            if (quests == null)
            {
                return;
            }

            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];
                if (quest == null || !quest.EverAccepted || knownAcceptedQuestIds.Contains(quest.id))
                {
                    continue;
                }

                RecordQuestAccepted(quest);
            }
        }

        /// <summary>
        /// Marks a quest as already handled by the acceptance path so the scanner does not produce a
        /// duplicate entry after the direct hook succeeds.
        /// </summary>
        private void MarkAcceptedQuestSeen(Quest quest)
        {
            if (quest != null)
            {
                knownAcceptedQuestIds.Add(quest.id);
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
    }
}

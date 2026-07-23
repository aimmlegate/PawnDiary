// Quest events — the accept/end hooks + the defensive accept-state scanner. The XML-scoped fan-out
// (snapshot the quest's rich context, dedup per quest+signal, choose all eligible pawns or one stable
// witness, plus the generic event-window lifecycle signal) moved to QuestFanoutSignal
// (Source/Ingestion/Sources/). What stays here is the thin orchestration that both the Harmony hooks
// and the scanner share: marking a quest as seen so the scanner does not double-record, mapping an
// end outcome to a signal, and submitting the fan-out. Ordinary acceptances are bookkeeping/window
// signals; an exact start-page window may own that proven edge. RecordQuestEnded maps Success ->
// "completed" / Fail -> "failed" terminal quest pages.
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
        /// event-window policy. Ordinary acceptances remain silent, while an exact XML start-page
        /// window may own this proven transition; completed/failed endings own terminal quest pages.
        /// </summary>
        internal void RecordQuestAccepted(Quest quest)
        {
            // Quest.Accept is canonical and the generated UI closure is only a fallback. Both may
            // observe one click, so only the first seen-set admission owns the lifecycle signal.
            if (!MarkAcceptedQuestSeen(quest)) return;
            DiaryEvents.Submit(new QuestFanoutSignal(quest, QuestEventData.SignalAccepted, "PawnDiary.Event.QuestAccepted"));
        }

        /// <summary>
        /// Records a quest ending. Called by the <see cref="QuestEndPatch"/> Harmony postfix on
        /// <c>Quest.End</c>. Only Success and Fail outcomes are forwarded; Success -> "completed",
        /// Fail -> "failed".
        /// </summary>
        internal void RecordQuestEnded(Quest quest, QuestEndOutcome outcome)
        {
            string signal = QuestSignalForOutcome(outcome);
            if (string.IsNullOrEmpty(signal))
            {
                return;
            }

            // A3.0 terminal void ownership: when a terminal void answer is actively being authored, its
            // dedicated ending page owns the monolith quest's success restatement, so the generic
            // completed fan-out is suppressed for that one quest only. Any other quest, a failure, an
            // inactive scope, or an actor who cannot author the ending is unaffected (the fan-out runs).
            if (outcome == QuestEndOutcome.Success
                && DiaryAnomalyPatches.VoidOutcomeHookReady
                && quest != null
                && VoidOutcomeScope.OwnsQuestId(quest.id))
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
        /// RimWorld UI or modded flows that mark a quest accepted without hitting our patch. Exact
        /// Royal Ascent is deliberately excluded because its reviewed lifecycle has an exact hook and
        /// must never infer a start later from polled state.
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

                if (RoyalAscentPolicy.IsExactQuestRoot(
                    quest.root?.defName, DiaryRoyaltyPolicy.Snapshot()))
                {
                    // Mark it observed so the generic fallback does not reconsider it every scan.
                    // If the exact hook did not fire, no callback-proven Phase-7 start exists.
                    knownAcceptedQuestIds.Add(quest.id);
                    continue;
                }

                RecordQuestAccepted(quest);
            }
        }

        /// <summary>
        /// Marks a quest as already handled by the acceptance path so the scanner does not produce a
        /// duplicate entry after the direct hook succeeds.
        /// </summary>
        private bool MarkAcceptedQuestSeen(Quest quest)
        {
            return quest != null && knownAcceptedQuestIds.Add(quest.id);
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

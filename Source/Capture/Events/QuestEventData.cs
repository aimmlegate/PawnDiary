// Payload + pure decision for a "quest lifecycle" event (Quest.Accept and Quest.End hooks). Quests
// are colony-wide: when the player accepts a quest, and again when the quest ends in success or
// failure, every eligible colonist gets their own solo diary entry so each survivor can react.
//
// One DiaryEventType.Quest value covers all three signals; the Signal field ("accepted",
// "completed", "failed") routes the event to the right XML group via ClassifyQuest(signal), which
// is exactly how ArrivalEventData carries a synthetic defName so one Decide can route many cases.
//
// Only ACCEPTED quests are recorded, per the requirement "only quest that accepted". Offered quests
// (QuestManager.Add) are ignored entirely. The Accept hook is the entry point; the End hook records
// completion/failure only for quests that were previously accepted (vanilla never ends an
// unaccepted quest, so no extra gating is needed here).
//
// Rich context (quest description, issuer faction defName, rewards summary) is captured upstream
// and embedded in the localized event text (description) and the game-context marker (rewards +
// faction). The event carries a "quest=" marker so the UI classifies it into the Quest domain.
// "unknown"/"none" are the English sentinels for absent faction/rewards (AGENTS.md section 12).
using System;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one colonist reacting to a quest lifecycle moment. Filled by
    /// DiaryGameComponent.RecordQuestAccepted / RecordQuestEnded inside the per-pawn fan-out loop.
    /// </summary>
    public class QuestEventData : DiaryEventData
    {
        /// <summary>Signal for a freshly accepted quest. Also the XML group classifier key.</summary>
        public const string SignalAccepted = "accepted";
        /// <summary>Signal for a quest that ended in QuestEndOutcome.Success.</summary>
        public const string SignalCompleted = "completed";
        /// <summary>Signal for a quest that ended in QuestEndOutcome.Fail.</summary>
        public const string SignalFailed = "failed";

        /// <summary>Sentinel substituted when the quest exposes no issuer faction.</summary>
        public const string FactionUnknown = "unknown";
        /// <summary>Sentinel substituted when the quest exposes no scannable rewards.</summary>
        public const string RewardsNone = "none";

        public override DiaryEventType EventType => DiaryEventType.Quest;

        /// <summary>The lifecycle signal: "accepted", "completed", or "failed". Also the key passed
        /// to InteractionGroups.ClassifyQuest so the right prompt group is selected.</summary>
        public string Signal;

        /// <summary>The quest's defName (QuestScriptDef.defName).</summary>
        public string DefName;

        /// <summary>The quest's cleaned display label, pre-resolved by the caller.</summary>
        public string Label;

        /// <summary>The issuer/requester faction's defName, or <see cref="FactionUnknown"/>.</summary>
        public string FactionDefName;

        /// <summary>Short cleaned reward summary, or <see cref="RewardsNone"/>.</summary>
        public string Rewards;

        /// <summary>
        /// Pure decision for one colonist's quest entry. An empty signal drops (defensive — the
        /// offered-but-not-accepted case never reaches this point, since only Accept/End hooks call
        /// the catalog). Otherwise eligible + user-enabled -> GenerateSolo.
        /// </summary>
        public static CaptureDecision Decide(QuestEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null)
            {
                return CaptureDecision.Drop;
            }

            // Defensive guard: a payload with no signal cannot route to any Quest group, so it drops.
            // This mirrors the "do not record offered-but-not-accepted quests" requirement: the hook
            // layer never constructs such a payload, but the pure layer enforces it independently.
            if (string.IsNullOrEmpty(data.Signal))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        /// <summary>
        /// Pure assembly of the quest game-context marker. The leading "quest=" marker is
        /// load-bearing: the UI parses it to classify the event into the Quest domain. The signal
        /// field routes prompt group selection (accepted/completed/failed). The quest's prose
        /// description is intentionally NOT embedded here — it is prose, so it lives in the localized
        /// event text instead. Field order is locked by tests in DiaryCapturePolicyTests.
        /// </summary>
        public static string BuildGameContext(string defName, string signal, string label, string factionDefName, string rewards)
        {
            return "quest=" + defName
                + "; signal=" + signal
                + "; label=" + label
                + "; faction=" + factionDefName
                + "; rewards=" + rewards;
        }
    }
}

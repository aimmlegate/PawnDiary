// Payload + pure decision for a "colony raid" event (the IncidentWorker_Raid hook). This is a
// MINIMAL colony-threatening incident source designed from scratch onto the Event Catalog. When a
// raid (RaidEnemy / RaidFriendly / RaidBeacon) or infestation executes on a map, each eligible
// colonist on that map gets their own solo diary entry so each survivor can write their personal
// take on the threat.
//
// The decision itself is intentionally minimal per the "minimal realization" brief: eligible +
// user-enabled -> GenerateSolo. The hook pre-computes raid-level facts (incident defName, raider
// faction defName, raid points, arrival mode, strategy) and copies them onto every per-pawn payload,
// exactly like MoodEventData copies the shared condition facts onto each colonist's payload.
// Colony-level dedup (one window per raid, keyed by incident/faction/points/map) lives in
// RecordRaid, not here.
//
// The event carries a "raid=" game-context marker so the UI classifies it into the Raid domain.
// "unknown" is the English sentinel for an absent faction defName (AGENTS.md section 12 carve-out:
// sentinels stay English on purpose).
using System;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one colonist living through a raid. Filled by
    /// DiaryGameComponent.RecordRaid inside its per-pawn loop. Shared raid facts (incident defName,
    /// raider faction, points) are copied onto every per-pawn payload because catalog dispatch is
    /// per-pawn — each Decide call must be self-contained.
    /// </summary>
    public class RaidEventData : DiaryEventData
    {
        /// <summary>Sentinel substituted when the raid carries no faction (AGENTS.md section 12
        /// English carve-out: structured sentinels are intentionally not localized).</summary>
        public const string FactionUnknown = "unknown";

        public override DiaryEventType EventType => DiaryEventType.Raid;

        /// <summary>The incident's defName (e.g. "RaidEnemy", "RaidFriendly", "Infestation").</summary>
        public string DefName;

        /// <summary>The raid's cleaned display label, pre-resolved by the caller. Embedded in the
        /// game-context marker so prompt policy has a human-readable category.</summary>
        public string Label;

        /// <summary>The raider faction's defName, or <see cref="FactionUnknown"/> when absent.</summary>
        public string FactionDefName;

        /// <summary>The raid's threat points as a pre-formatted string (caller rounds to int).</summary>
        public string Points;

        /// <summary>Optional PawnsArrivalModeDef defName, saved as text for prompt policy.</summary>
        public string ArrivalModeDefName;

        /// <summary>Optional RaidStrategyDef defName, saved as text for prompt policy.</summary>
        public string StrategyDefName;

        /// <summary>
        /// Pure decision for one colonist's raid entry. Eligibility + the user's group toggle are the
        /// only gates; everything else records. This matches the MoodEvent minimal shape: a
        /// colony-wide incident fans out to each colonist, and the catalog sees one event at a time.
        /// </summary>
        public static CaptureDecision Decide(RaidEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null)
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
        /// Pure assembly of the raid's per-pawn game-context marker. The leading "raid=" marker is
        /// load-bearing: the UI parses it to classify the event into the Raid domain. Field order is
        /// locked by tests in DiaryCapturePolicyTests.
        /// </summary>
        public static string BuildGameContext(
            string defName, string label, string factionDefName, string points,
            string arrivalModeDefName = null, string strategyDefName = null)
        {
            string context = "raid=" + defName
                + "; label=" + label
                + "; faction=" + factionDefName
                + "; points=" + points;

            if (!string.IsNullOrWhiteSpace(arrivalModeDefName))
            {
                context += "; arrival_mode=" + arrivalModeDefName;
            }

            if (!string.IsNullOrWhiteSpace(strategyDefName))
            {
                context += "; strategy=" + strategyDefName;
            }

            return context;
        }

        /// <summary>
        /// Pure policy for the raid LLM delay. Normal raids usually spend time approaching, so a
        /// positive XML delay waits before generation. Drop-pod raids and infestations are immediate
        /// threats and bypass the wait.
        /// </summary>
        public static bool ShouldDelayGeneration(
            string incidentDefName, string arrivalModeDefName, string strategyDefName, int delayTicks)
        {
            if (delayTicks <= 0
                || ContainsIgnoreCase(incidentDefName, "Infestation")
                || ContainsIgnoreCase(arrivalModeDefName, "Drop")
                || ContainsIgnoreCase(strategyDefName, "Drop"))
            {
                return false;
            }

            return true;
        }

        private static bool ContainsIgnoreCase(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.IsNullOrWhiteSpace(token)
                && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

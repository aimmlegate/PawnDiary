// Payload + pure decision for a "mood-affecting game condition started" event (the
// GameConditionManager.RegisterCondition hook). This is the second existing source migrated to
// the Event Catalog and the first one with multi-pawn fan-out: a single GameCondition (aurora,
// eclipse, psychic drone, toxic fallout, ...) triggers one RecordMoodEvent call, which then creates
// a separate solo DiaryEvent for each eligible colonist on affected maps.
//
// The catalog dispatch happens per pawn: RecordMoodEvent iterates affected colonists, builds one
// MoodEventData per pawn, and asks the catalog for a decision. The condition-level dedup (one
// window per GameCondition.uniqueID) and the per-pawn duplicate check (a pawn on multiple maps
// during a transition) stay in RecordMoodEvent — they are per-source-call and per-loop invariants,
// not per-event decisions.
//
// The decision itself is trivial: eligible + user-enabled → GenerateSolo. There is no token
// filter and no magnitude threshold for MoodEvent today; once a condition qualifies and is not
// deduped, every eligible colonist gets an entry. (Per-pawn mood-impact direction — positive/
// negative/neutral — is computed by the caller via DiaryContextBuilder.DetermineMoodImpact and
// passed into the payload, because some conditions affect different sexes differently.)
using System.Globalization;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one colonist experiencing a mood-affecting GameCondition. Filled by
    /// DiaryGameComponent.RecordMoodEvent inside its per-pawn loop. The shared condition facts
    /// (defName, label) are copied onto every per-pawn payload because the catalog dispatch is
    /// per-pawn — each Decide call must be self-contained.
    /// </summary>
    public class MoodEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.MoodEvent;

        /// <summary>The GameCondition's defName (e.g. "Aurora", "PsychicDrone").</summary>
        public string DefName;

        /// <summary>The GameCondition's cleaned display label, pre-resolved by the caller. Used by
        /// BuildGameContext to embed in the game-context marker string.</summary>
        public string Label;

        /// <summary>Per-pawn mood-impact direction ("positive"/"negative"/"neutral"). Computed by
        /// the caller because conditions like PsychicSuppressorMale affect sexes differently and
        /// the classification needs RimWorld pawn state. The Decider does NOT branch on this — it
        /// only carries it through to BuildGameContext.</summary>
        public string MoodImpact;

        /// <summary>
        /// Pure decision for one colonist's mood-event entry. Eligibility + the user's per-def
        /// toggle are the only gates; everything else records. This matches the pre-refactor
        /// behavior, which had no token filter or magnitude threshold at the per-pawn level.
        /// </summary>
        public static CaptureDecision Decide(MoodEventData data, CaptureContext ctx)
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
        /// Pure assembly of the mood-event's per-pawn game-context marker string. The leading
        /// "mood_event=" marker is load-bearing: the UI parses it to classify the event into the
        /// MoodEvent domain. Format locked by tests in DiaryCapturePolicyTests.
        /// </summary>
        public static string BuildGameContext(string defName, string label, string moodImpact)
        {
            return "mood_event=" + defName
                + "; label=" + label
                + "; mood_impact=" + moodImpact;
        }
    }
}

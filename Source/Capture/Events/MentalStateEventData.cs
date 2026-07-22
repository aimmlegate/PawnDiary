// Payload + pure decision for a "pawn entered a mental state" event (the
// MentalStateHandler.TryStartMentalState hook). This is the fourth existing source migrated to
// the Event Catalog and the FIRST pair source: a social fight (MentalStateDef "SocialFighting")
// between two eligible pawns is recorded as a pairwise event with both POV entries, while every
// other accepted break (berserk, sad wandering, insult spree, ...) is recorded as a solo event
// from the breaking pawn's point of view. Vanilla IdeoChange is one special lifecycle: its PostStart
// immediately replaces itself with a silent Wander_OwnRoom/Wander_Sad companion state. That nested
// implementation detail is suppressed here so the outer crisis remains the one player-visible page.
//
// The decision logic mirrors the pre-refactor RecordMentalState body byte-for-byte:
// 1. eligibility + user toggle gate,
// 2. "is this a social fight?" branch:
//    - defName == "SocialFighting" AND otherPawn is eligible AND otherPawn != pawn → GeneratePair
//    - otherwise → GenerateSolo.
//
// The otherPawn's identity is carried on the payload (OtherPawnId) so the sink can build the
// pairwise event. The cleaned OtherPawnLabel is carried separately for the solo-break "target="
// game-context field (pair events do not include a target field because both participants are POV
// pawns).
//
// Dedup lives in RecordMentalState (impure): pair events dedup by pair-key (collapsed across the
// mirrored second call), solo breaks dedup by pawn+defName. Both run after the catalog decision,
// matching the pre-refactor order.
namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one mental-state event. Filled by DiaryGameComponent.RecordMentalState
    /// from the live Pawn + MentalStateDef (+ optional otherPawn from the hook).
    /// </summary>
    internal class MentalStateEventData : DiaryEventData
    {
        /// <summary>RimWorld's stable defName for the social-fight mental state. Hardcoded because
        /// this is a specific vanilla defName we classify on, not an XML-tunable token.</summary>
        public const string SocialFightingDefName = "SocialFighting";

        /// <summary>Exact vanilla lifecycle tokens used only to collapse IdeoChange's nested wander
        /// implementation detail. They are strings so loading Pawn Diary never requires Ideology.</summary>
        public const string IdeoChangeDefName = "IdeoChange";
        public const string WanderOwnRoomDefName = "Wander_OwnRoom";
        public const string WanderSadDefName = "Wander_Sad";

        public override DiaryEventType EventType => DiaryEventType.MentalState;

        /// <summary>The mental state's defName (e.g. "Berserk", "SocialFighting", "SadWander").</summary>
        public string DefName;

        /// <summary>The other pawn's id when the hook provided one (social fights and targeted
        /// breaks). Empty/null for untargeted breaks. Used by Decide for the pair branch and by
        /// the sink to build the pairwise event.</summary>
        public string OtherPawnId;

        /// <summary>True when OtherPawn is non-null AND diary-eligible. Pre-computed by the caller
        /// because pawn-eligibility reads RimWorld state. Decide uses this to decide pair vs
        /// solo: a SocialFighting event with an ineligible counterpart falls back to solo.</summary>
        public bool OtherPawnEligible;

        /// <summary>The cleaned display label of the other pawn, used by BuildSoloGameContext for
        /// the optional "target=" field. Empty for pair events and untargeted breaks. Pre-cleaned
        /// by the caller via DiaryLineCleaner.CleanLine.</summary>
        public string OtherPawnLabel;

        /// <summary>
        /// Recognizes vanilla's exact current/requested/silent signature for the companion transition
        /// normally started inside <c>MentalState_IdeoChange.PostStart</c>. The predicate cannot inspect
        /// the caller stack, so an external mod deliberately issuing that identical transition while
        /// IdeoChange is current is accepted as the same companion. Ordinary sad wandering, unrelated
        /// nested states, non-silent transitions, and different modded DefNames remain candidates.
        /// </summary>
        public static bool ShouldSuppressNestedCompanion(
            string currentDefName,
            string requestedDefName,
            bool transitionSilently)
        {
            if (!transitionSilently
                || !string.Equals(currentDefName, IdeoChangeDefName,
                    System.StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(requestedDefName, WanderOwnRoomDefName,
                       System.StringComparison.Ordinal)
                || string.Equals(requestedDefName, WanderSadDefName,
                    System.StringComparison.Ordinal);
        }

        /// <summary>
        /// Pure decision for a mental-state event. Order matches the pre-refactor RecordMentalState:
        /// eligibility + user toggle gate, then "is this a pair social fight?" branch. A social fight
        /// requires the defName match, an eligible counterpart, and counterpart != self — otherwise
        /// the event falls through to a solo break entry.
        /// </summary>
        public static CaptureDecision Decide(MentalStateEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null)
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled)
            {
                return CaptureDecision.Drop;
            }

            if (IsSocialFightPair(data))
            {
                return CaptureDecision.GeneratePair;
            }

            return CaptureDecision.GenerateSolo;
        }

        /// <summary>
        /// True when this event is a social fight between two distinct eligible pawns. Matches the
        /// pre-refactor isSocialFight condition exactly. Kept separate from Decide so the sink can
        /// reuse the same check when forming the dedup key (the pair dedup key includes both pawn
        /// ids in canonical order).
        /// </summary>
        public static bool IsSocialFightPair(MentalStateEventData data)
        {
            if (data == null)
            {
                return false;
            }

            if (!string.Equals(data.DefName, SocialFightingDefName, System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!data.OtherPawnEligible)
            {
                return false;
            }

            if (string.IsNullOrEmpty(data.OtherPawnId))
            {
                return false;
            }

            // otherPawn == self would be a degenerate call; the hook should never emit it but we
            // guard anyway so the catalog never produces a self-pair.
            return !string.Equals(data.OtherPawnId, data.PawnId, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// The transient dedup key for this mental-state event (raw, source-prefixed). A social fight
        /// dedups by canonical pair key (so the mirrored second call collapses); a solo break dedups
        /// per pawn + defName. Lifted out of the old RecordMentalState unchanged. The matching dedup
        /// window differs by shape — the signal reads socialFight vs mentalBreak ticks accordingly.
        /// </summary>
        public string DedupKey()
        {
            return IsSocialFightPair(this)
                ? "fight|" + CanonicalPairKey(PawnId, OtherPawnId)
                : "break|" + PawnId + "|" + DefName;
        }

        /// <summary>
        /// Pure assembly of a PAIR mental-state event's game-context marker (social fights). The
        /// leading "mental_state=" marker is load-bearing for UI domain classification. No
        /// "target=" field because both participants are POV pawns. Reason is appended only if
        /// non-empty (caller pre-cleans it via DiaryLineCleaner.CleanLine).
        /// </summary>
        public static string BuildPairGameContext(string defName, string label, string cleanedReason)
        {
            string context = "mental_state=" + defName + "; label=" + label;
            if (!string.IsNullOrWhiteSpace(cleanedReason))
            {
                context += "; reason=" + cleanedReason;
            }
            return context;
        }

        /// <summary>
        /// Pure assembly of a SOLO mental-state event's game-context marker (mental breaks). Same
        /// leading marker as pair events, plus an optional "target=" field when the break is
        /// directed at another pawn (e.g. an insult rage aimed at someone), plus an optional
        /// reason. Caller pre-cleans the label, target, and reason.
        /// </summary>
        public static string BuildSoloGameContext(
            string defName, string label, string cleanedTargetLabel, string cleanedReason)
        {
            string context = "mental_state=" + defName + "; label=" + label;
            if (!string.IsNullOrWhiteSpace(cleanedTargetLabel))
            {
                context += "; target=" + cleanedTargetLabel;
            }
            if (!string.IsNullOrWhiteSpace(cleanedReason))
            {
                context += "; reason=" + cleanedReason;
            }
            return context;
        }
    }
}

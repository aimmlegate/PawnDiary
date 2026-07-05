// Payload + pure decision for an "external mod event" — a moment ANOTHER MOD reports through the
// public integration API (PawnDiary.Integration.PawnDiaryApi.SubmitEvent). Pawn Diary never hooks
// the other mod itself: an adapter mod observes its own systems and pushes a plain request at us.
//
// The eventKey string plays the role a defName plays for native sources: External-domain
// DiaryInteractionGroupDefs match it (usually shipped as XML by the adapter mod), and classification
// is REQUIRED-match like Romance — an eventKey no group claims records nothing. That keeps all
// prompt policy in XML and makes a garbage submission harmless.
//
// This file must stay RimWorld-free (no Verse/RimWorld/Unity usings): it is linked into the
// standalone DiaryCapturePolicyTests project. The impure half (live Pawn reads, localized text,
// event creation) lives in Source/Ingestion/Sources/ExternalEventSignal.cs.
using System;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one external-mod event. Filled by ExternalEventSignal from the validated
    /// API request, then handed to Decide() for the pure decision.
    /// </summary>
    public class ExternalEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.External;

        /// <summary>The adapter's stable classifier string (e.g. "rjw_casual_encounter"). Plays the
        /// defName role: External-domain groups match it, and it is stored on the DiaryEvent.</summary>
        public string EventKey;

        /// <summary>The submitting mod's package id, for log attribution. Never affects the
        /// decision; classification is by EventKey only.</summary>
        public string SourceId;

        /// <summary>The subject pawn's id (mirrors the base PawnId; kept explicit so pair logic
        /// reads symmetrically with PartnerPawnId).</summary>
        public string SubjectPawnId;

        /// <summary>The optional partner pawn's id. Empty means a solo event.</summary>
        public string PartnerPawnId;

        /// <summary>True when the subject pawn is diary-eligible (computed by the signal).</summary>
        public bool SubjectEligible;

        /// <summary>True when a partner was supplied AND that pawn is diary-eligible.</summary>
        public bool PartnerEligible;

        /// <summary>True when an External-domain group claimed the EventKey. The signal only builds
        /// a payload after classification succeeds, but the flag stays on the payload so the pure
        /// tests can exercise the "nobody claimed this key" drop without a DefDatabase.</summary>
        public bool HasGroup;

        /// <summary>
        /// True for ordinary external events, where a group is required to prove XML prompt policy
        /// claims the key. Direct-text injection owns its prose already, so it sets this false and may
        /// use a group only when one exists for label/toggle/styling.
        /// </summary>
        public bool GroupRequired = true;

        /// <summary>
        /// Pure decision for an external event. Drops when: incomplete data, a required group is
        /// missing, the group's signal/user gates are off, or the subject pawn is ineligible. A
        /// supplied, eligible, distinct partner upgrades the event to a pairwise entry (both POVs);
        /// anything else records a solo entry from the subject's POV.
        /// </summary>
        public static CaptureDecision Decide(ExternalEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.EventKey))
            {
                return CaptureDecision.Drop;
            }

            if (data.GroupRequired && !data.HasGroup)
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.SignalEnabled || !ctx.UserEnabled)
            {
                return CaptureDecision.Drop;
            }

            // The subject must qualify for a diary. Unlike Romance (a mutual milestone), an external
            // event is subject-centric: an ineligible partner just downgrades pair -> solo.
            if (!data.SubjectEligible)
            {
                return CaptureDecision.Drop;
            }

            bool distinctPartner = !string.IsNullOrEmpty(data.PartnerPawnId)
                && !string.Equals(data.SubjectPawnId, data.PartnerPawnId, StringComparison.Ordinal);
            if (distinctPartner && data.PartnerEligible)
            {
                return CaptureDecision.GeneratePair;
            }

            return CaptureDecision.GenerateSolo;
        }

        /// <summary>
        /// The transient dedup key for this external event (raw, source-prefixed). Solo events key
        /// per subject; pair events use the canonical order-independent pair key so an adapter that
        /// mirrors the call for both participants collapses to one window. Adapters that need a
        /// different collapse rule pass a custom dedupKey on the request instead.
        /// </summary>
        public string DedupKey()
        {
            string pawnPart = string.IsNullOrEmpty(PartnerPawnId)
                ? SubjectPawnId
                : CanonicalPairKey(SubjectPawnId, PartnerPawnId);
            return "external|" + EventKey + "|" + pawnPart;
        }

        /// <summary>
        /// Pure assembly of the external game-context marker. The leading "external=" marker is
        /// load-bearing: DiaryEventDomainClassifier maps it back to the External domain for prompt
        /// policy and display styling. `source=` carries the submitting mod's package id, and any
        /// pre-cleaned "key=value" extra lines from the adapter follow verbatim.
        /// </summary>
        public static string BuildGameContext(string eventKey, string sourceId, string extraContext)
        {
            string context = "external=" + eventKey + "; source=" + sourceId;
            if (!string.IsNullOrEmpty(extraContext))
            {
                context += "; " + extraContext;
            }

            return context;
        }
    }
}

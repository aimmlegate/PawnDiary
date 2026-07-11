// Pure conversion from a validated editorial decision to the compact context attached to Pawn
// Diary's normal pairwise prompt submission. Keeping this planning separate makes the important
// invariants testable: ignore submits nothing, related carries one actual event id, standalone does
// not, the root-id dedup key stays frozen, and assessment focus supplements rather than replaces
// transcript evidence.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Collections.Generic;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Plain submission plan consumed by the impure ConversationTracker.</summary>
    public sealed class ConversationSubmissionPlan
    {
        public bool ShouldSubmit;
        public string DedupKey;
        public string Assessment;
        public string RelatedEventId;
        public string Reason;
        public string Focus;
        public List<string> ExtraContext = new List<string>();
    }

    /// <summary>Plans accepted conversation context without touching Pawn Diary or RimWorld.</summary>
    public static class ConversationSubmissionPlanner
    {
        /// <summary>Builds one prompt-submission plan; invalid/ignore input returns ShouldSubmit=false.</summary>
        public static ConversationSubmissionPlan Build(
            Conversation conversation,
            int transcriptLineCap,
            ConversationAssessmentResult assessment)
        {
            ConversationSubmissionPlan plan = new ConversationSubmissionPlan();
            if (conversation == null || assessment == null
                || (assessment.Decision != ConversationAssessmentTokens.Related
                    && assessment.Decision != ConversationAssessmentTokens.Standalone))
            {
                return plan;
            }

            if (assessment.Decision == ConversationAssessmentTokens.Related
                && string.IsNullOrEmpty(assessment.EventId))
            {
                return plan;
            }

            string focus = UnicodeText.CleanOneLine(assessment.Focus);
            if (focus.Length == 0)
            {
                return plan;
            }

            plan.ShouldSubmit = true;
            plan.DedupKey = "rimtalkbridge|" + (conversation.RootTalkId ?? string.Empty);
            plan.Assessment = assessment.Decision;
            plan.RelatedEventId = assessment.Decision == ConversationAssessmentTokens.Related
                ? assessment.EventId : string.Empty;
            plan.Reason = assessment.Reason ?? "other";
            plan.Focus = focus;
            plan.ExtraContext = ConversationContext.BuildExtraContext(conversation, transcriptLineCap);
            plan.ExtraContext.Add("conversation_assessment=" + plan.Assessment);
            if (plan.RelatedEventId.Length > 0)
            {
                plan.ExtraContext.Add("related_event_id=" + plan.RelatedEventId);
            }

            plan.ExtraContext.Add("conversation_reason=" + plan.Reason);
            plan.ExtraContext.Add("conversation_focus=" + plan.Focus);
            if (plan.Assessment == ConversationAssessmentTokens.Related)
            {
                plan.ExtraContext.Add("avoid_related_event_recap=true");
            }

            return plan;
        }
    }
}

// XML-owned policy for the RimTalk conversation editorial funnel. RimWorld loads this Def from the
// bridge's 1.6/Defs folder, then the impure coordinator copies its values into plain DTOs consumed by
// the pure scorer/formatter. Prompt prose and keyword lists are DefInjected-localizable.
//
// If the XML is missing or malformed, Current returns conservative numeric fallbacks with no prompt
// or lexicon. Semantic assessment therefore waits instead of emitting unlocalized or unsafe prose.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Tunable scoring, queue, budget, formatting, overlap, lexicon, and prompt policy.</summary>
    public class ConversationAssessmentPolicyDef : Def
    {
        public const string PolicyDefName = "RimTalk_ConversationAssessment";

        public int candidateScoreThreshold = 5;
        public int strictLocalRecordThreshold = 8;

        public int chargedSocialWeight = 3;
        public int reciprocalChargedWeight = 2;
        public int userTalkWeight = 2;
        public int alternationWeight = 2;
        public int alternationThreshold = 2;
        public int mediumLengthWeight = 1;
        public int mediumLengthLines = 4;
        public int longLengthWeight = 1;
        public int longLengthLines = 8;
        public int firstKeywordCategoryWeight = 2;
        public int additionalKeywordCategoryWeight = 1;
        public int maxKeywordCategories = 4;
        public int recentEventOverlapPenalty = 2;
        public int announcementOnlyPenalty = 3;
        public int samePairQueuedPenalty = 2;

        public int maxQueuedCandidates = 12;
        public int maxCandidatesPerBatch = 6;
        public int maxCandidatesPerPair = 2;
        public int maxBatchesPerDay = 2;
        public int minBatchGapTicks = 15000;
        public int candidateExpiryTicks = 60000;
        public int perPawnConversationCooldownTicks = 60000;

        public int assessmentTranscriptLines = 4;
        public int assessmentLineChars = 160;
        public int assessmentInputChars = 3600;
        public int assessmentMaxTokens = 180;
        public int assessmentFocusChars = 200;
        public int recentEventCount = 3;
        public int recentEventWindowTicks = 60000;
        public int overlapMinimumTokenChars = 3;
        public bool useCharacterTrigramOverlap = true;
        public int maxEditableReactionTerms = 64;
        public int maxEditableReactionTermChars = 80;
        public int assessmentPromptOverrideChars = 6000;

        public float recentEventOverlapThreshold = 0.45f;

        public List<string> disclosureTerms = new List<string>();
        public List<string> commitmentTerms = new List<string>();
        public List<string> conflictTerms = new List<string>();
        public List<string> reconciliationTerms = new List<string>();

        public string assessmentSystemPrompt = string.Empty;

        private static readonly ConversationAssessmentPolicyDef Fallback =
            new ConversationAssessmentPolicyDef { defName = PolicyDefName };

        /// <summary>Returns the loaded policy or a conservative no-prompt fallback.</summary>
        public static ConversationAssessmentPolicyDef Current
        {
            get
            {
                return DefDatabase<ConversationAssessmentPolicyDef>.GetNamedSilentFail(PolicyDefName) ?? Fallback;
            }
        }

        /// <summary>Copies scoring policy into a pure DTO, applying defensive lower bounds.</summary>
        public ConversationCandidatePolicyOptions CandidateOptions()
        {
            return new ConversationCandidatePolicyOptions
            {
                CandidateScoreThreshold = candidateScoreThreshold,
                ChargedSocialWeight = chargedSocialWeight,
                ReciprocalChargedWeight = reciprocalChargedWeight,
                UserTalkWeight = userTalkWeight,
                AlternationWeight = alternationWeight,
                AlternationThreshold = Math.Max(1, alternationThreshold),
                MediumLengthWeight = mediumLengthWeight,
                MediumLengthLines = Math.Max(2, mediumLengthLines),
                LongLengthWeight = longLengthWeight,
                LongLengthLines = Math.Max(Math.Max(2, mediumLengthLines), longLengthLines),
                FirstKeywordCategoryWeight = firstKeywordCategoryWeight,
                AdditionalKeywordCategoryWeight = additionalKeywordCategoryWeight,
                MaxKeywordCategories = Math.Max(0, Math.Min(5, maxKeywordCategories)),
                RecentEventOverlapPenalty = Math.Abs(recentEventOverlapPenalty),
                RecentEventOverlapThreshold = Clamp01(recentEventOverlapThreshold),
                AnnouncementOnlyPenalty = Math.Abs(announcementOnlyPenalty),
                SamePairQueuedPenalty = Math.Abs(samePairQueuedPenalty)
            };
        }

        /// <summary>Copies localized category lists into the pure matcher contract.</summary>
        public ConversationKeywordLexicon KeywordLexicon()
        {
            return new ConversationKeywordLexicon
            {
                DisclosureTerms = disclosureTerms ?? new List<string>(),
                CommitmentTerms = commitmentTerms ?? new List<string>(),
                ConflictTerms = conflictTerms ?? new List<string>(),
                ReconciliationTerms = reconciliationTerms ?? new List<string>(),
                CustomTerms = new List<string>()
            };
        }

        /// <summary>
        /// Applies a validated saved flat-list override. Invalid hand-edited settings fail closed to
        /// the localized XML lexicon rather than disabling selection or throwing during a tick pass.
        /// </summary>
        public ConversationKeywordLexicon KeywordLexicon(string overrideCsv)
        {
            ConversationKeywordLexicon defaults = KeywordLexicon();
            if (string.IsNullOrWhiteSpace(overrideCsv))
            {
                return defaults;
            }

            ConversationReactionTermsValidationResult validation =
                ConversationReactionTermsEditor.Validate(
                    overrideCsv,
                    Math.Max(1, maxEditableReactionTerms),
                    Math.Max(1, maxEditableReactionTermChars));
            return validation.IsValid
                ? ConversationReactionTermsEditor.ApplyOverride(defaults, validation.Terms)
                : defaults;
        }

        /// <summary>Canonical localized XML terms shown when no saved override exists.</summary>
        public string DefaultReactionTermsCsv()
        {
            return ConversationReactionTermsEditor.ToCsv(KeywordLexicon());
        }

        /// <summary>Resolves a saved player prompt or the localized DefInjected default.</summary>
        public string AssessmentPrompt(string overridePrompt)
        {
            string editorial = ConversationAssessmentPromptEditor.Resolve(
                assessmentSystemPrompt,
                overridePrompt,
                Math.Max(1, assessmentPromptOverrideChars));
            // Preserve the conservative missing-Def fallback: a wire schema without editorial policy
            // is not enough evidence to spend a request or trust a classification.
            return string.IsNullOrWhiteSpace(editorial)
                ? string.Empty
                : ConversationAssessmentWireContract.Compose(editorial);
        }

        /// <summary>Copies compact batch limits into the pure formatter contract.</summary>
        public ConversationAssessmentFormatOptions FormatOptions()
        {
            return new ConversationAssessmentFormatOptions
            {
                MaxCandidates = Math.Max(1, maxCandidatesPerBatch),
                TranscriptLines = Math.Max(1, assessmentTranscriptLines),
                LineChars = Math.Max(16, assessmentLineChars),
                InputChars = Math.Max(256, assessmentInputChars),
                RecentEventCount = Math.Max(0, recentEventCount)
            };
        }

        /// <summary>Reports invalid XML policy early during Def loading.</summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (candidateScoreThreshold <= 0)
            {
                yield return "candidateScoreThreshold must be greater than zero.";
            }

            if (strictLocalRecordThreshold < candidateScoreThreshold)
            {
                yield return "strictLocalRecordThreshold must be at least candidateScoreThreshold.";
            }

            if (maxQueuedCandidates <= 0 || maxCandidatesPerBatch <= 0
                || maxCandidatesPerBatch > maxQueuedCandidates)
            {
                yield return "queue/batch limits must be positive and maxCandidatesPerBatch cannot exceed maxQueuedCandidates.";
            }

            if (maxCandidatesPerPair <= 0 || maxCandidatesPerPair > maxQueuedCandidates)
            {
                yield return "maxCandidatesPerPair must be positive and no larger than maxQueuedCandidates.";
            }

            if (maxBatchesPerDay <= 0 || minBatchGapTicks < 0 || candidateExpiryTicks <= 0
                || perPawnConversationCooldownTicks <= 0)
            {
                yield return "assessment cadence requires positive day/cooldown/expiry values and a non-negative batch gap.";
            }

            if (assessmentTranscriptLines <= 0 || assessmentLineChars <= 0
                || assessmentInputChars <= 0 || assessmentMaxTokens <= 0 || assessmentFocusChars <= 0)
            {
                yield return "assessment transcript/input/output limits must be positive.";
            }

            if (recentEventCount < 0 || recentEventWindowTicks <= 0
                || recentEventOverlapThreshold < 0f || recentEventOverlapThreshold > 1f)
            {
                yield return "recent-event limits require a positive window/count >= 0 and overlap threshold in [0,1].";
            }

            if (maxEditableReactionTerms <= 0 || maxEditableReactionTermChars <= 0
                || assessmentPromptOverrideChars <= 0)
            {
                yield return "editable reaction-term and assessment-prompt limits must be positive.";
            }

            if (string.IsNullOrWhiteSpace(assessmentSystemPrompt))
            {
                yield return "assessmentSystemPrompt must not be blank.";
            }
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}

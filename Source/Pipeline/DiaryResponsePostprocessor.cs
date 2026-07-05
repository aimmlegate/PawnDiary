// Pure response postprocessing for the diary pipeline.
//
// LlmResponseParser extracts the provider-visible text and removes reasoning transcripts. This
// postprocessor applies the prompt-time DiaryResponseRules to produce the typed result that the save
// adapter can persist. Do not read game state, XML, settings, Verse, UnityEngine, IO, or Translate().
namespace PawnDiary
{
    /// <summary>
    /// Converts parser output plus response rules into a typed persistence plan.
    /// </summary>
    internal static class DiaryResponsePostprocessor
    {
        public static DiaryResponsePlan ApplySuccess(string rawVisibleResponse, DiaryResponseRules rules)
        {
            rules = rules ?? new DiaryResponseRules();
            string generated = LlmResponseParser.CleanGeneratedText(
                rawVisibleResponse,
                rules.maxTokens,
                !rules.trimIncompleteSentence || rules.isTitle);

            return new DiaryResponsePlan
            {
                eventId = rules.eventId,
                povRole = rules.targetRole,
                success = true,
                generatedText = generated,
                rawVisibleResponse = rawVisibleResponse,
                titleText = rules.isTitle ? generated : string.Empty,
                responseRules = rules
            };
        }

        public static DiaryResponsePlan ApplyFailure(string error, DiaryResponseRules rules)
        {
            rules = rules ?? new DiaryResponseRules();
            return new DiaryResponsePlan
            {
                eventId = rules.eventId,
                povRole = rules.targetRole,
                success = false,
                error = error,
                responseRules = rules
            };
        }
    }
}

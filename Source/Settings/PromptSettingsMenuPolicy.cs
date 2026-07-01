// Pure settings-menu policy for keeping the two prompt-editing subpages distinct. The RimWorld IMGUI
// drawing code lives in PawnDiaryMod.* files, but simple routing/display decisions live here so tests
// can lock down the intended menu contract without loading Unity or Verse.
using System;

namespace PawnDiary
{
    /// <summary>Pure helpers that decide how prompt settings should be split across menu surfaces.</summary>
    internal static class PromptSettingsMenuPolicy
    {
        /// <summary>
        /// True when a DiaryPromptTemplateDef field is a raw per-template text override. Empty values in
        /// these fields inherit shared/default prompt text, but Prompt policy must not display that
        /// inherited text because Shared/event prompts already owns the shared prompt editors.
        /// </summary>
        public static bool IsTemplateTextOverrideField(string fieldName)
        {
            return string.Equals(fieldName, "systemPrompt", StringComparison.Ordinal)
                || string.Equals(fieldName, "finalInstruction", StringComparison.Ordinal)
                || string.Equals(fieldName, "recipientFinalInstruction", StringComparison.Ordinal);
        }

        /// <summary>
        /// Template text override fields should show only their raw XML/settings value. Returning false
        /// here is the dedup rule: do not mirror inherited shared prompt text into Prompt policy.
        /// </summary>
        public static bool TemplateFieldShouldShowInheritedFallback(string fieldName)
        {
            return false;
        }
    }
}

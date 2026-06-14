// Assembles the final prompt text sent to the model for each generation mode
// (single POV, paired sequential POV, solo). Static helpers, no state. Split out of
// DiaryGameComponent.cs. See DOCUMENTATION.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public static class DiaryPromptBuilder
    {
        // Prompts intentionally omit any field that is empty or "normal" (see AppendField),
        // so the model only ever sees signal — no "health: healthy", no weather indoors, etc.
        public static string BuildSequentialInteractionPrompt(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent.solo)
            {
                return BuildSoloPrompt(diaryEvent);
            }

            string initiatorEntry = string.Equals(povRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase)
                ? DiaryContextBuilder.CleanLine(diaryEvent.initiatorGeneratedText)
                : null;

            return BuildPairPrompt(diaryEvent, povRole, initiatorEntry);
        }

        public static string BuildInteractionPrompt(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent.solo)
            {
                return BuildSoloPrompt(diaryEvent);
            }

            return BuildPairPrompt(diaryEvent, povRole, null);
        }

        private static string BuildPairPrompt(DiaryEvent diaryEvent, string povRole, string initiatorEntry)
        {
            bool isInitiator = string.Equals(povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase);
            string otherName = isInitiator ? diaryEvent.recipientName : diaryEvent.initiatorName;
            string povText = diaryEvent.TextForRole(povRole);
            string povSummary = isInitiator ? diaryEvent.initiatorPawnSummary : diaryEvent.recipientPawnSummary;

            List<string> lines = new List<string> { "event: " + EventNoun(diaryEvent) };

            AppendField(lines, "pov", diaryEvent.NameForRole(povRole));
            AppendField(lines, "role", isInitiator ? "initiator" : "recipient");
            AppendField(lines, "with", otherName);
            AppendField(lines, "what you saw", povText);
            AppendField(lines, "instruction", diaryEvent.instruction);
            AppendField(lines, "you", povSummary);
            AppendField(lines, "setting", diaryEvent.SurroundingsForRole(povRole));
            AppendField(lines, "relationship", diaryEvent.ContinuityForRole(povRole));
            AppendField(lines, "initiator diary (hidden context)", initiatorEntry);

            DiaryPromptDef p = DiaryPrompts.Current;
            string instruction = string.IsNullOrWhiteSpace(initiatorEntry)
                ? p.singlePovInstruction
                : p.recipientFollowupInstruction;

            return string.Join("\n", lines.ToArray()) + "\n\n" + instruction;
        }

        private static string BuildSoloPrompt(DiaryEvent diaryEvent)
        {
            List<string> lines = new List<string> { "event: " + EventNoun(diaryEvent) };

            AppendField(lines, "pov", diaryEvent.initiatorName);
            AppendField(lines, "what happened", diaryEvent.initiatorText);
            AppendField(lines, "instruction", diaryEvent.instruction);
            AppendField(lines, "you", diaryEvent.initiatorPawnSummary);
            AppendField(lines, "setting", diaryEvent.initiatorSurroundings);
            AppendField(lines, "relationship", diaryEvent.initiatorContinuity);

            return string.Join("\n", lines.ToArray()) + "\n\n" + DiaryPrompts.Current.singlePovInstruction;
        }

        private static string EventNoun(DiaryEvent diaryEvent)
        {
            string label = DiaryContextBuilder.CleanLine(diaryEvent.interactionLabel);
            return string.IsNullOrWhiteSpace(label) ? "social event" : label.ToLowerInvariant();
        }

        // Adds "key: value" only when the value carries real signal. Empty strings and
        // placeholder values ("none", "n/a", "unknown") are skipped so they cost no tokens.
        private static void AppendField(List<string> lines, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string trimmed = value.Trim();
            if (trimmed == "none" || trimmed == "n/a" || trimmed == "unknown")
            {
                return;
            }

            lines.Add(key + ": " + trimmed);
        }
    }
}

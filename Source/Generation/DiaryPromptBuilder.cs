// Assembles the final prompt text sent to the model for each generation mode
// (single POV, dual POV, solo). Static helpers, no state. Split out of
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
        public static string BuildDualInteractionPrompt(DiaryEvent diaryEvent)
        {
            List<string> lines = new List<string> { "event: " + EventNoun(diaryEvent) + " (two viewpoints)" };

            AppendField(lines, "initiator", diaryEvent.initiatorName);
            AppendField(lines, "recipient", diaryEvent.recipientName);
            if (string.Equals(diaryEvent.initiatorText, diaryEvent.recipientText, StringComparison.OrdinalIgnoreCase))
            {
                AppendField(lines, "what happened", diaryEvent.initiatorText);
            }
            else
            {
                AppendField(lines, "initiator saw", diaryEvent.initiatorText);
                AppendField(lines, "recipient saw", diaryEvent.recipientText);
            }

            AppendField(lines, "instruction", diaryEvent.instruction);
            AppendField(lines, "initiator profile", diaryEvent.initiatorPawnSummary);
            AppendField(lines, "recipient profile", diaryEvent.recipientPawnSummary);
            if (string.Equals(diaryEvent.initiatorSurroundings, diaryEvent.recipientSurroundings, StringComparison.OrdinalIgnoreCase))
            {
                AppendField(lines, "setting", diaryEvent.initiatorSurroundings);
            }
            else
            {
                AppendField(lines, "initiator setting", diaryEvent.initiatorSurroundings);
                AppendField(lines, "recipient setting", diaryEvent.recipientSurroundings);
            }

            AppendField(lines, "initiator relationship", diaryEvent.initiatorContinuity);
            AppendField(lines, "recipient relationship", diaryEvent.recipientContinuity);

            return string.Join("\n", lines.ToArray())
                + "\n\nWrite two short first-person diary entries, one from each pawn's point of view, following the instruction."
                + " Each pawn only knows what they could perceive. Output EXACTLY in this format and nothing else:"
                + "\n[INITIATOR]"
                + "\n<" + diaryEvent.initiatorName + "'s diary entry>"
                + "\n[RECIPIENT]"
                + "\n<" + diaryEvent.recipientName + "'s diary entry>";
        }

        public static string BuildInteractionPrompt(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent.solo)
            {
                return BuildSoloPrompt(diaryEvent);
            }

            bool isInitiator = string.Equals(povRole, DiaryEvent.InitiatorRole, StringComparison.OrdinalIgnoreCase);
            string otherName = isInitiator ? diaryEvent.recipientName : diaryEvent.initiatorName;
            string povText = diaryEvent.TextForRole(povRole);
            string otherText = isInitiator ? diaryEvent.recipientText : diaryEvent.initiatorText;
            string povSummary = isInitiator ? diaryEvent.initiatorPawnSummary : diaryEvent.recipientPawnSummary;

            List<string> lines = new List<string> { "event: " + EventNoun(diaryEvent) };

            AppendField(lines, "pov", diaryEvent.NameForRole(povRole));
            AppendField(lines, "with", otherName);
            AppendField(lines, "what happened", povText);
            if (!string.Equals(povText, otherText, StringComparison.OrdinalIgnoreCase))
            {
                AppendField(lines, "their view", otherText);
            }

            AppendField(lines, "instruction", diaryEvent.instruction);
            AppendField(lines, "you", povSummary);
            AppendField(lines, "setting", diaryEvent.SurroundingsForRole(povRole));
            AppendField(lines, "relationship", diaryEvent.ContinuityForRole(povRole));

            return string.Join("\n", lines.ToArray());
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

            return string.Join("\n", lines.ToArray());
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

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
        public static string BuildSequentialInteractionPrompt(DiaryEvent diaryEvent, string povRole, string personaRule)
        {
            if (diaryEvent.solo)
            {
                return BuildSoloPrompt(diaryEvent, personaRule);
            }

            string initiatorEntry = string.Equals(povRole, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase)
                ? DiaryContextBuilder.CleanLine(diaryEvent.initiatorGeneratedText)
                : null;

            return BuildPairPrompt(diaryEvent, povRole, initiatorEntry, personaRule);
        }

        public static string BuildInteractionPrompt(DiaryEvent diaryEvent, string povRole, string personaRule)
        {
            if (diaryEvent.solo)
            {
                return BuildSoloPrompt(diaryEvent, personaRule);
            }

            return BuildPairPrompt(diaryEvent, povRole, null, personaRule);
        }

        /// <summary>
        /// Builds the neutral, persona-independent prompt used only for colonist death
        /// descriptions. It deliberately omits persona, relationship continuity, and first-person
        /// POV fields because this output is a factual death note, not a diary entry.
        /// </summary>
        public static string BuildDeathDescriptionPrompt(DiaryEvent diaryEvent)
        {
            string victimRole = ContextValue(diaryEvent.gameContext, "death_victim_role");
            string victimName = ContextValue(diaryEvent.gameContext, "death_victim");
            if (string.IsNullOrWhiteSpace(victimName))
            {
                victimName = NameForContextRole(diaryEvent, victimRole);
            }

            List<string> lines = new List<string> { "event: colonist death" };
            AppendField(lines, "deceased", victimName);
            AppendField(lines, "what happened", diaryEvent.neutralText);
            AppendField(lines, "death facts", diaryEvent.gameContext);
            AppendField(lines, "deceased pawn", PawnSummaryForContextRole(diaryEvent, victimRole));
            AppendField(lines, "setting", SurroundingsForContextRole(diaryEvent, victimRole));

            return string.Join("\n", lines.ToArray()) + "\n\n" + DiaryPrompts.Current.deathDescriptionInstruction;
        }

        /// <summary>
        /// Builds the neutral, persona-independent prompt used for the first diary entry: how this
        /// pawn became part of the colony. Starting pawns get scenario context; later pawns get the
        /// SetFaction/join facts captured at runtime.
        /// </summary>
        public static string BuildArrivalDescriptionPrompt(DiaryEvent diaryEvent)
        {
            string pawnName = ContextValue(diaryEvent.gameContext, "arrival_pawn");
            if (string.IsNullOrWhiteSpace(pawnName))
            {
                pawnName = diaryEvent.initiatorName;
            }

            List<string> lines = new List<string> { "event: colonist arrival" };
            AppendField(lines, "colonist", pawnName);
            AppendField(lines, "what happened", diaryEvent.neutralText);
            AppendField(lines, "arrival facts", diaryEvent.gameContext);
            AppendField(lines, "colonist pawn", diaryEvent.initiatorPawnSummary);
            AppendField(lines, "setting", diaryEvent.initiatorSurroundings);

            return string.Join("\n", lines.ToArray()) + "\n\n" + DiaryPrompts.Current.arrivalDescriptionInstruction;
        }

        private static string BuildPairPrompt(DiaryEvent diaryEvent, string povRole, string initiatorEntry, string personaRule)
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
            // Persona is a writing-style rule from the pawn's saved preset, not a gameplay fact.
            AppendField(lines, "persona", personaRule);
            AppendField(lines, "setting", diaryEvent.SurroundingsForRole(povRole));
            // Atmosphere is a short emotional anchor combining mood + relationship context.
            AppendField(lines, "atmosphere", diaryEvent.AtmosphereForRole(povRole));
            AppendField(lines, "relationship", diaryEvent.ContinuityForRole(povRole));
            AppendField(lines, "my last opener (not repeat)", diaryEvent.LastOpenerForRole(povRole));
            // Burning passion only for important events (not chit chat etc.)
            if (diaryEvent.IsImportant())
            {
                AppendField(lines, "burning passion", isInitiator ? diaryEvent.initiatorBurningPassion : diaryEvent.recipientBurningPassion);
            }

            // Weapon only for important or combat events
            if (diaryEvent.ShouldShowWeapon())
            {
                AppendField(lines, "weapon", isInitiator ? diaryEvent.initiatorWeapon : diaryEvent.recipientWeapon);
            }

            AppendField(lines, "initiator diary (hidden context)", initiatorEntry);

            DiaryPromptDef p = DiaryPrompts.Current;
            string instruction = string.IsNullOrWhiteSpace(initiatorEntry)
                ? p.singlePovInstruction
                : p.recipientFollowupInstruction;

            return string.Join("\n", lines.ToArray()) + "\n\n" + instruction;
        }

        private static string BuildSoloPrompt(DiaryEvent diaryEvent, string personaRule)
        {
            List<string> lines = new List<string> { "event: " + EventNoun(diaryEvent) };

            AppendField(lines, "pov", diaryEvent.initiatorName);
            AppendField(lines, "what happened", diaryEvent.initiatorText);
            AppendField(lines, "instruction", diaryEvent.instruction);
            AppendField(lines, "you", diaryEvent.initiatorPawnSummary);
            // Solo events use the same persona field as pairwise entries for prompt-lab parity.
            AppendField(lines, "persona", personaRule);
            AppendField(lines, "setting", diaryEvent.initiatorSurroundings);
            // Atmosphere is a short emotional anchor for the model.
            AppendField(lines, "atmosphere", diaryEvent.initiatorAtmosphere);
            AppendField(lines, "relationship", diaryEvent.initiatorContinuity);
            AppendField(lines, "my last opener (not repeat)", diaryEvent.initiatorLastOpener);
            // Burning passion only for important events (not chit chat etc.)
            if (diaryEvent.IsImportant())
            {
                AppendField(lines, "burning passion", diaryEvent.initiatorBurningPassion);
            }

            // Weapon only for important or combat events
            if (diaryEvent.ShouldShowWeapon())
            {
                AppendField(lines, "weapon", diaryEvent.initiatorWeapon);
            }

            return string.Join("\n", lines.ToArray()) + "\n\n" + DiaryPrompts.Current.singlePovInstruction;
        }

        private static string EventNoun(DiaryEvent diaryEvent)
        {
            string label = DiaryContextBuilder.CleanLine(diaryEvent.interactionLabel);
            if (string.IsNullOrWhiteSpace(label))
            {
                return "PawnDiary.Prompt.SocialEvent".Translate();
            }

            return label.ToLowerInvariant();
        }

        private static string NameForContextRole(DiaryEvent diaryEvent, string role)
        {
            if (string.Equals(role, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.recipientName;
            }

            return diaryEvent.initiatorName;
        }

        private static string PawnSummaryForContextRole(DiaryEvent diaryEvent, string role)
        {
            if (string.Equals(role, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.recipientPawnSummary;
            }

            return diaryEvent.initiatorPawnSummary;
        }

        private static string SurroundingsForContextRole(DiaryEvent diaryEvent, string role)
        {
            if (string.Equals(role, DiaryEvent.RecipientRole, StringComparison.OrdinalIgnoreCase))
            {
                return diaryEvent.recipientSurroundings;
            }

            return diaryEvent.initiatorSurroundings;
        }

        private static string ContextValue(string context, string key)
        {
            if (string.IsNullOrWhiteSpace(context) || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string prefix = key + "=";
            string[] parts = context.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring(prefix.Length).Trim();
                }
            }

            return string.Empty;
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

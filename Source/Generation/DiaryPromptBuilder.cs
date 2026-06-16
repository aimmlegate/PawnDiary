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

        // Trailer appended to every title-generation user message. Lives in code (not in
        // DiaryPromptDef / Keyed) because it is the structured "what to return" instruction for
        // the model — the same carve-out as singlePovInstruction / dualInstruction, which are
        // also fixed English trailers on the user side. Keeping it constant here avoids loading
        // a new Keyed string for one short sentence.
        private const string TitleTrailer =
            "\n\nReturn one short title (3-8 words) for this diary entry. Output only the title \u2014 no quotes, no period, no labels, no commentary.";

        /// <summary>
        /// Builds the user message for the opt-in title-generation follow-up call. The system
        /// prompt lives on the request and tells the model to return a title; the user message
        /// carries the diary entry to summarize. Uses the LLM-generated entry when available,
        /// else falls back to the raw game text. The title prompt is intentionally small and
        /// cheap — see <see cref="DiaryGameComponent.Generation.QueueTitleRequest"/>.
        /// </summary>
        public static string BuildTitlePrompt(DiaryEvent diaryEvent, string povRole)
        {
            if (diaryEvent == null)
            {
                return TitleTrailer.TrimStart('\n', 'r');
            }

            // Prefer the polished LLM output; fall back to the raw game text when the main entry
            // hasn't finished yet (rare — the title call is only fired after a successful main
            // entry, but the fallback keeps the request self-contained).
            // DisplayTextForRole is the public accessor: returns generated if non-empty, else raw.
            string entryText = diaryEvent.DisplayTextForRole(povRole);
            if (string.IsNullOrWhiteSpace(entryText))
            {
                return TitleTrailer.TrimStart('\n', 'r');
            }

            return entryText + TitleTrailer;
        }

        /// <summary>
        /// Strips the common noise a model adds around a short title (markdown bullets, quote
        /// marks, a trailing period) and truncates to a sensible UI length. Returns empty when
        /// the input is too short to be a title; callers then fall back to the first sentence
        /// of the generated entry.
        /// </summary>
        public static string CleanTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string cleaned = raw.Trim();
            // Strip a single leading " or ' wrapping the whole title.
            if (cleaned.Length >= 2
                && (cleaned[0] == '"' || cleaned[0] == '\u201c')
                && (cleaned[cleaned.Length - 1] == '"' || cleaned[cleaned.Length - 1] == '\u201d'))
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();
            }
            else if (cleaned.Length >= 2 && cleaned[0] == '\'' && cleaned[cleaned.Length - 1] == '\'')
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();
            }

            // Strip a leading markdown bullet ("- ", "* ", "# ") — some models decorate lists.
            if (cleaned.StartsWith("- ", StringComparison.Ordinal))
            {
                cleaned = cleaned.Substring(2).TrimStart();
            }
            else if (cleaned.StartsWith("* ", StringComparison.Ordinal))
            {
                cleaned = cleaned.Substring(2).TrimStart();
            }
            else if (cleaned.StartsWith("# ", StringComparison.Ordinal))
            {
                cleaned = cleaned.Substring(2).TrimStart();
            }
            else if (cleaned.StartsWith("#", StringComparison.Ordinal) && cleaned.Length > 1 && char.IsLetterOrDigit(cleaned[1]))
            {
                cleaned = cleaned.Substring(1).TrimStart();
            }

            // Collapse internal whitespace so a model that emits "We  sat   by the fire"
            // doesn't end up with stray double-spaces in the diary header.
            cleaned = Regex.Replace(cleaned, @"\s+", " ");

            // Drop a trailing period (or similar) — the system prompt already forbids it,
            // but small models sometimes add one anyway.
            char last = cleaned.Length > 0 ? cleaned[cleaned.Length - 1] : '\0';
            if (last == '.' || last == '!' || last == '?' || last == '\u2026')
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 1).TrimEnd();
            }

            const int MaxTitleLength = 80;
            if (cleaned.Length > MaxTitleLength)
            {
                cleaned = cleaned.Substring(0, MaxTitleLength).TrimEnd();
            }

            return cleaned;
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
            // Tone is the event's emotional register (terrifying, funny, tender...), set per group.
            AppendField(lines, "tone", diaryEvent.ToneDirective());
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
            // Tone is the event's emotional register (terrifying, funny, tender...), set per group.
            AppendField(lines, "tone", diaryEvent.ToneDirective());
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

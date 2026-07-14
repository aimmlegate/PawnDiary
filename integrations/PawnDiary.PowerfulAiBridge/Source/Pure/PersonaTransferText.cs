// Pure formatting and change-detection helpers for transferring a Powerful AI Integration persona
// into Pawn Diary. Runtime reflection is deliberately elsewhere; this file accepts plain strings so
// it can be tested without RimWorld, Verse, Unity, or either mod assembly.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiaryPowerfulAiBridge.Pure
{
    /// <summary>Plain snapshot of the semantic text fields in one Powerful AI persona.</summary>
    public sealed class PowerfulAiPersonaSnapshot
    {
        public string dialoguePersona = string.Empty;
        public string speechHabits = string.Empty;
        public string storyRole = string.Empty;
        public string characterPrompt = string.Empty;
        public string personaPreset = string.Empty;
    }

    /// <summary>Builds the direct psychotype rule, LLM input, and stable source fingerprint.</summary>
    public static class PersonaTransferText
    {
        /// <summary>
        /// Copies every nonblank semantic persona field into a compact structured block. The labels are
        /// stable prompt-schema words; source prose is preserved after whitespace normalization.
        /// </summary>
        public static string BuildDirectRule(PowerfulAiPersonaSnapshot snapshot, int maxCharacters)
        {
            if (snapshot == null || maxCharacters <= 0)
            {
                return null;
            }

            List<string> lines = new List<string>();
            Add(lines, "persona", snapshot.dialoguePersona);
            Add(lines, "speech habits", snapshot.speechHabits);
            Add(lines, "story role", snapshot.storyRole);
            Add(lines, "character prompt", snapshot.characterPrompt);
            Add(lines, "persona preset", snapshot.personaPreset);

            if (lines.Count == 0)
            {
                return null;
            }

            string text = string.Join("\n", lines.ToArray());
            return text.Length <= maxCharacters ? text : SafePrefix(text, maxCharacters).TrimEnd();
        }

        /// <summary>Uses the same complete structured block as the LLM transform input.</summary>
        public static string BuildTransformInput(PowerfulAiPersonaSnapshot snapshot, int maxCharacters)
        {
            return BuildDirectRule(snapshot, maxCharacters);
        }

        /// <summary>
        /// Returns a deterministic compact fingerprint. Unlike string.GetHashCode, this is stable across
        /// processes and save/load, so an unchanged persona never re-spends LLM tokens.
        /// </summary>
        public static string StableFingerprint(string value)
        {
            string text = value ?? string.Empty;
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    hash ^= (byte)(c & 0xff);
                    hash *= 16777619u;
                    hash ^= (byte)(c >> 8);
                    hash *= 16777619u;
                }

                return hash.ToString("x8");
            }
        }

        private static void Add(List<string> lines, string label, string value)
        {
            string cleaned = Clean(value);
            if (cleaned.Length > 0)
            {
                lines.Add(label + ": " + cleaned);
            }
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            bool pendingSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        // Avoid ending on a dangling UTF-16 high surrogate when the defensive cap cuts emoji/text.
        private static string SafePrefix(string value, int length)
        {
            int take = Math.Min(value.Length, Math.Max(0, length));
            if (take > 0 && take < value.Length && char.IsHighSurrogate(value[take - 1]))
            {
                take--;
            }

            return value.Substring(0, take);
        }
    }
}

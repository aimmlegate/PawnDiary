// Reflection-only reader for Powerful AI Integration's public saved persona model. Keeping target
// types out of our metadata means an absent or updated target mod cannot TypeLoadException this bridge;
// a signature mismatch simply disables the read and logs one warning.
using System;
using System.Collections;
using System.Reflection;
using PawnDiaryPowerfulAiBridge.Pure;
using Verse;

namespace PawnDiaryPowerfulAiBridge
{
    /// <summary>Reads one existing PAI persona without creating or modifying target-mod state.</summary>
    internal static class PowerfulAiReflection
    {
        private const string ModTypeName = "DynamicRoleStoryteller.DynamicRoleStorytellerMod";
        private static bool searched;
        private static bool warned;
        private static FieldInfo settingsField;
        private static FieldInfo charactersField;

        public static bool TryReadPersona(Pawn pawn, out PowerfulAiPersonaSnapshot snapshot)
        {
            snapshot = null;
            if (pawn == null || !EnsureSurface())
            {
                return false;
            }

            try
            {
                object settings = settingsField.GetValue(null);
                IEnumerable characters = settings == null ? null : charactersField.GetValue(settings) as IEnumerable;
                if (characters == null)
                {
                    return false;
                }

                string pawnId = pawn.GetUniqueLoadID();
                foreach (object character in characters)
                {
                    if (character == null || !MatchesPawn(character, pawnId))
                    {
                        continue;
                    }

                    snapshot = new PowerfulAiPersonaSnapshot
                    {
                        dialoguePersona = ReadString(character, "dialoguePrompt"),
                        speechHabits = ReadString(character, "dialogueSpeechHabits"),
                        storyRole = ReadString(character, "storyRole"),
                        characterPrompt = ReadString(character, "prompt"),
                        personaPreset = ReadString(character, "dialoguePersonaPreset")
                    };
                    return true;
                }
            }
            catch (Exception e)
            {
                WarnOnce("could not read a persona: " + e.GetType().Name + ": " + e.Message);
            }

            return false;
        }

        /// <summary>Clears cached reflection state for tests or a new process-level discovery pass.</summary>
        public static void Reset()
        {
            searched = false;
            warned = false;
            settingsField = null;
            charactersField = null;
        }

        private static bool EnsureSurface()
        {
            if (searched)
            {
                return settingsField != null && charactersField != null;
            }

            searched = true;
            Type modType = FindType(ModTypeName);
            settingsField = modType?.GetField("Settings", BindingFlags.Public | BindingFlags.Static);
            Type settingsType = settingsField?.FieldType;
            charactersField = settingsType?.GetField("storyCharacters", BindingFlags.Public | BindingFlags.Instance);
            if (settingsField == null || charactersField == null)
            {
                WarnOnce("the installed Powerful AI Integration build does not expose the expected persona fields; the bridge stays idle.");
                return false;
            }

            return true;
        }

        private static bool MatchesPawn(object character, string pawnId)
        {
            Type type = character.GetType();
            FieldInfo idField = type.GetField("boundPawnId", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo enabledField = type.GetField("enabled", BindingFlags.Public | BindingFlags.Instance);
            if (idField == null)
            {
                return false;
            }

            bool enabled = enabledField == null || !(enabledField.GetValue(character) is bool value) || value;
            string boundId = idField.GetValue(character) as string;
            return enabled && string.Equals(boundId, pawnId, StringComparison.Ordinal);
        }

        private static string ReadString(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            return field == null ? string.Empty : field.GetValue(instance) as string ?? string.Empty;
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static void WarnOnce(string message)
        {
            if (warned)
            {
                return;
            }

            warned = true;
            Log.Warning(PawnDiaryPowerfulAiBridgeMod.LogPrefix + " " + message);
        }
    }
}

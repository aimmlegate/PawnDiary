// Stable cache keys for RimTalk persona synchronization. These keys are persisted in save data, so
// they must not use string.GetHashCode (which may change between processes/runtimes).
using System;
using System.Globalization;

namespace PawnDiaryRimTalkBridge.Pure
{
    /// <summary>Builds deterministic source keys for direct and transformed persona synchronization.</summary>
    internal static class PersonaSyncKey
    {
        // Transformed text is cached in the save. Bump only the direction whose prompt meaning changed
        // so old generated prose is replaced once without disturbing direct sync or the other direction.
        private const string ImportTransformPromptVersion = "2";
        private const string ExportTransformPromptVersion = "2";

        /// <summary>Stable key for RimTalk -> Pawn Diary input, transform mode, and prompt revision.</summary>
        public static string ForImport(string source, bool transform)
        {
            string keyMaterial = (source ?? string.Empty) + "\nimport-transform=" + (transform ? "1" : "0");
            if (transform)
            {
                keyMaterial += "\nimport-prompt-version=" + ImportTransformPromptVersion;
            }

            return Hash(keyMaterial);
        }

        /// <summary>Stable key for Pawn Diary -> RimTalk input, transform mode, prompt revision, and modifier.</summary>
        public static string ForExport(string source, bool transform, PersonaPromptModifier modifier)
        {
            string keyMaterial = (source ?? string.Empty) + "\nexport-transform=" + (transform ? "1" : "0")
                + "\nmodifier=" + ((int)modifier).ToString(CultureInfo.InvariantCulture);
            if (transform)
            {
                keyMaterial += "\nexport-prompt-version=" + ExportTransformPromptVersion;
            }

            return Hash(keyMaterial);
        }

        /// <summary>64-bit FNV-1a over UTF-16 code units, formatted as fixed lowercase hexadecimal.</summary>
        public static string Hash(string value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            string text = value ?? string.Empty;
            unchecked
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    hash ^= (byte)c;
                    hash *= prime;
                    hash ^= (byte)(c >> 8);
                    hash *= prime;
                }
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}

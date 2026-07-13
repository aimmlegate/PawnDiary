// Stable cache keys for RimTalk persona synchronization. These keys are persisted in save data, so
// they must not use string.GetHashCode (which may change between processes/runtimes).
using System;
using System.Globalization;

namespace PawnDiaryRimTalkBridge.Pure
{
    /// <summary>Builds deterministic source keys for direct and transformed persona synchronization.</summary>
    internal static class PersonaSyncKey
    {
        /// <summary>Stable key for RimTalk -> Pawn Diary import input and transform mode.</summary>
        public static string ForImport(string source, bool transform)
        {
            return Hash((source ?? string.Empty) + "\nimport-transform=" + (transform ? "1" : "0"));
        }

        /// <summary>Stable key for Pawn Diary -> RimTalk input, transform mode, and bounded modifier.</summary>
        public static string ForExport(string source, bool transform, PersonaPromptModifier modifier)
        {
            return Hash((source ?? string.Empty) + "\nexport-transform=" + (transform ? "1" : "0")
                + "\nmodifier=" + ((int)modifier).ToString(CultureInfo.InvariantCulture));
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

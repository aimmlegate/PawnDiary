// Exact pawn ownership checks for transient dictionaries whose keys are pipe-delimited.
//
// Brainwipe removes only the wiped pawn's short-lived scanner/dedup state. Stable load IDs such as
// "Thing_Human123" are complete key tokens; substring checks are unsafe because Pawn_1 must never
// match Pawn_10. Colony fan-out keys are deliberately exempt even when a ritual key happens to name
// the pawn: that key owns the shared occurrence, not one colonist's autobiography.
//
// This helper contains no Verse/RimWorld/Unity types, so the exact-token contract is covered by the
// standalone capture-policy tests. New to C#/RimWorld? See AGENTS.md.
using System;

namespace PawnDiary.Capture
{
    /// <summary>Classifies exact pawn ownership in pipe-delimited transient key stores.</summary>
    internal static class PawnScopedTransientKeyPolicy
    {
        /// <summary>
        /// True only when the first complete token is <paramref name="pawnId"/>. Progression scanner
        /// state uses <c>pawnId|category...</c>, so this removes one pawn without prefix collisions.
        /// </summary>
        public static bool StartsWithPawnToken(string key, string pawnId)
        {
            string owner = NormalizePawnId(pawnId);
            return owner.Length > 0
                && key != null
                && key.Length > owner.Length
                && key[owner.Length] == '|'
                && string.CompareOrdinal(key, 0, owner, 0, owner.Length) == 0;
        }

        /// <summary>
        /// True when a structurally pawn-owned recent-event key names the exact pawn in an ownership
        /// field. The prefix/position allowlist is intentional: opaque source keys and custom external
        /// keys may contain ID-looking text that is not POV ownership, and therefore survive.
        /// </summary>
        public static bool RecentEventKeyBelongsToPawn(string key, string pawnId)
        {
            string owner = NormalizePawnId(pawnId);
            if (owner.Length == 0 || string.IsNullOrEmpty(key))
            {
                return false;
            }

            int prefixLength = key.IndexOf('|');
            if (prefixLength < 0)
            {
                return false;
            }

            if (PrefixEquals(key, prefixLength, "event-type"))
                return TokenEquals(key, 2, owner) || TokenEquals(key, 3, owner);
            if (PrefixEquals(key, prefixLength, "thought")
                || PrefixEquals(key, prefixLength, "thoughtprogression")
                || PrefixEquals(key, prefixLength, "break")
                || PrefixEquals(key, prefixLength, "royal-permit")
                || PrefixEquals(key, prefixLength, "progression-gene")
                || PrefixEquals(key, prefixLength, "progression-trait")
                || PrefixEquals(key, prefixLength, "ability")
                || PrefixEquals(key, prefixLength, "anniversary-birthday")
                || PrefixEquals(key, prefixLength, "anniversary-arrival")
                || PrefixEquals(key, prefixLength, "anniversary-death")
                || PrefixEquals(key, prefixLength, "anniversary-record")
                || PrefixEquals(key, prefixLength, "biotech-deathrest-interrupted")
                || PrefixEquals(key, prefixLength, "mechanitor-mechlink-install")
                || PrefixEquals(key, prefixLength, "mechanitor-mechlink-remove")
                || PrefixEquals(key, prefixLength, "mechanitor-first-mech")
                || PrefixEquals(key, prefixLength, "mechanitor-first-combat")
                || PrefixEquals(key, prefixLength, "mechanitor-mech-loss")
                || PrefixEquals(key, prefixLength, "mechanitor-boss-called")
                || PrefixEquals(key, prefixLength, "mechanitor-boss-defeated")
                || PrefixEquals(key, prefixLength, "royalty-psylink")
                || PrefixEquals(key, prefixLength, "royalty-title"))
                return TokenEquals(key, 1, owner);
            if (PrefixEquals(key, prefixLength, "hediff"))
                return TokenEquals(key, 2, owner);
            if (PrefixEquals(key, prefixLength, "fight")
                || PrefixEquals(key, prefixLength, "romance")
                || PrefixEquals(key, prefixLength, "biotech-bond"))
                return TokenEquals(key, 1, owner) || TokenEquals(key, 2, owner);
            if (PrefixEquals(key, prefixLength, "tale"))
                return TokenEquals(key, 2, owner) || TokenEquals(key, 3, owner);
            if (PrefixEquals(key, prefixLength, "external"))
            {
                // External eventKey is adapter-controlled and may itself contain '|'. Only accept the
                // documented unambiguous solo/pair shapes; a longer key is conservatively opaque rather
                // than mistaking an eventKey segment for a pawn owner.
                int tokenCount = TokenCount(key);
                return tokenCount == 3 && TokenEquals(key, 2, owner)
                    || tokenCount == 4
                        && (TokenEquals(key, 2, owner) || TokenEquals(key, 3, owner));
            }
            if (PrefixEquals(key, prefixLength, "royal-heir-appointment")
                || PrefixEquals(key, prefixLength, "royal-succession"))
            {
                // These pages belong only to the heir (token 2). The title holder/deceased pawn in
                // token 1 is shared source context; wiping that pawn must not reopen the heir's page.
                return TokenEquals(key, 2, owner);
            }

            // Colony fan-out keys, external-custom keys, and opaque occurrence identities all land
            // here. A one-pawn reset must never reopen their shared/global suppression window.
            return false;
        }

        private static string NormalizePawnId(string pawnId)
        {
            string value = (pawnId ?? string.Empty).Trim();
            // Pipe is the schema delimiter, so a value containing it cannot be one stable pawn token.
            return value.IndexOf('|') >= 0 ? string.Empty : value;
        }

        private static bool TokenEquals(string key, int wantedIndex, string expected)
        {
            int index = 0;
            int tokenStart = 0;
            while (tokenStart <= key.Length)
            {
                int delimiter = key.IndexOf('|', tokenStart);
                int tokenEnd = delimiter < 0 ? key.Length : delimiter;
                if (index == wantedIndex)
                {
                    int tokenLength = tokenEnd - tokenStart;
                    return tokenLength == expected.Length
                        && string.CompareOrdinal(
                            key, tokenStart, expected, 0, expected.Length) == 0;
                }
                if (delimiter < 0) return false;
                tokenStart = delimiter + 1;
                index++;
            }
            return false;
        }

        private static int TokenCount(string key)
        {
            if (key == null) return 0;
            int count = 1;
            for (int i = 0; i < key.Length; i++)
            {
                if (key[i] == '|') count++;
            }
            return count;
        }

        private static bool PrefixEquals(string key, int prefixLength, string expected)
        {
            return prefixLength == expected.Length
                && string.CompareOrdinal(key, 0, expected, 0, expected.Length) == 0;
        }
    }
}

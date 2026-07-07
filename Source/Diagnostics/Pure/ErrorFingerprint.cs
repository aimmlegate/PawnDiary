// Pure, deterministic fingerprint for an error message. Two crashes from the same code path should
// collapse to one fingerprint even when their line numbers, addresses, or counts differ, so the
// reporter can send each distinct problem only once and a server can group reports across players.
//
// We use FNV-1a rather than string.GetHashCode() on purpose: .NET randomizes GetHashCode per process,
// which would make the same crash hash differently on every machine and defeat cross-player grouping.
// FNV-1a is tiny, dependency-free, and identical everywhere.
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PawnDiary
{
    /// <summary>
    /// Computes a stable dedupe key for a scrubbed error string.
    /// </summary>
    internal static class ErrorFingerprint
    {
        // Hex addresses and digit runs (line numbers, offsets, "x3" counts) are the volatile parts of
        // an otherwise-identical stack, so they are normalized away before hashing.
        private static readonly Regex HexAddress = new Regex(@"0x[0-9a-fA-F]+", RegexOptions.Compiled);
        private static readonly Regex DigitRun = new Regex(@"[0-9]+", RegexOptions.Compiled);

        /// <summary>How many leading lines of the message define its identity (message + top frames).</summary>
        private const int SignificantLines = 6;

        /// <summary>
        /// Returns a 16-hex-character fingerprint of <paramref name="scrubbedMessage"/>. Deterministic:
        /// the same normalized text always yields the same value on any machine.
        /// </summary>
        public static string Compute(string scrubbedMessage)
        {
            string normalized = Normalize(scrubbedMessage);
            return Fnv1a64Hex(normalized);
        }

        /// <summary>
        /// Reduces a message to its identity: the first few lines with addresses/numbers blanked out.
        /// </summary>
        private static string Normalize(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            string[] rawLines = message.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            List<string> kept = new List<string>();
            for (int i = 0; i < rawLines.Length && kept.Count < SignificantLines; i++)
            {
                string line = rawLines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                line = HexAddress.Replace(line, "0x#");
                line = DigitRun.Replace(line, "#");
                kept.Add(line);
            }

            return string.Join("\n", kept.ToArray());
        }

        /// <summary>64-bit FNV-1a hash of the UTF-8 bytes, formatted as lowercase hex.</summary>
        private static string Fnv1a64Hex(string text)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offsetBasis;
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= prime;
            }

            return hash.ToString("x16");
        }
    }
}

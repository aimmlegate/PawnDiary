// MemorySettingsCommitPolicy.cs — pure predecessor comparison for durable M5 settings writes.
//
// File IO and platform locking stay in MemorySettingsDurableWriter. This small rule remains detached
// so stale-predecessor rejection can be proven without Verse or a real settings directory.
using System;

namespace PawnDiary
{
    internal static class MemorySettingsCommitPolicy
    {
        /// <summary>True only when existence and, when present, exact verified bytes still agree.</summary>
        public static bool PredecessorMatches(
            bool expectedExists,
            string expectedSha256,
            bool currentExists,
            string currentSha256)
        {
            return currentExists == expectedExists
                && (!currentExists || string.Equals(
                    currentSha256 ?? string.Empty,
                    expectedSha256 ?? string.Empty,
                    StringComparison.Ordinal));
        }
    }
}

// Public read-only DTO for adapters that want Pawn Diary's main context reads in one call.
// The bundle composes existing prompt-free snapshots; it never carries live RimWorld objects,
// prompts, raw provider responses, or mutable game state.
//
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;

namespace PawnDiary.Integration
{
    /// <summary>
    /// One pawn's writing style, structured pawn summary, prompt-enchantment candidates, and recent
    /// generated diary context bundled for adapter convenience.
    /// </summary>
    public sealed class DiaryContextBundleSnapshot
    {
        /// <summary>The pawn's base saved diary writing style, or null if unavailable.</summary>
        public DiaryWritingStyleSnapshot writingStyle;

        /// <summary>The structured pawn-summary facts Pawn Diary would feed its own prompts.</summary>
        public DiaryPawnSummarySnapshot pawnSummary;

        /// <summary>Prompt-enchantment candidates prepared for this pawn right now.</summary>
        public List<DiaryPromptEnchantmentCandidateSnapshot> promptEnchantments =
            new List<DiaryPromptEnchantmentCandidateSnapshot>();

        /// <summary>Recent completed generated diary prose summaries for this pawn.</summary>
        public DiaryContextSnapshot recentContext = new DiaryContextSnapshot();
    }
}

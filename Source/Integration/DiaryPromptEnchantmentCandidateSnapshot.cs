// Public read-only DTO for integration adapters that want the prompt-enchantment candidates Pawn
// Diary prepared for a pawn. This is the input side of the enchantment
// machinery: the post-suppression, post-multiplier candidate set the planner chooses among, not the
// single rolled winner. See design/EXTERNAL_API_CAPABILITIES.md §3.8.
//
// Keep this class plain: fields only, primitives/strings/lists only, no live RimWorld objects.
//
// Why a public mirror of the internal PromptEnchantmentCandidate? The internal type lives in the
// PawnDiary namespace and is shared with the planner; it must not cross the public boundary, so we
// copy into this snapshot DTO. The From() factory is the one mapping point and is unit-tested.
//
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;

namespace PawnDiary.Integration
{
    /// <summary>
    /// One collected prompt-enchantment option, exposed for the public integration API. Fields mirror
    /// the internal <see cref="PromptEnchantmentCandidate"/> exactly; lists are independent copies so
    /// a later mutation of the internal candidate never leaks into a snapshot the caller already holds.
    /// </summary>
    public sealed class DiaryPromptEnchantmentCandidateSnapshot
    {
        /// <summary>Resolved selection weight after live-state adjustment and normal-context multipliers.</summary>
        public float weight;

        /// <summary>defName of the hediff this candidate came from, when applicable. Empty otherwise.</summary>
        public string sourceHediffDefName = string.Empty;

        /// <summary>Localized priority text (e.g. "high-priority context").</summary>
        public string priorityText = string.Empty;

        /// <summary>Localized condition line (e.g. "in agony (left leg)").</summary>
        public string conditionText = string.Empty;

        /// <summary>Localized live-state impact cues (e.g. "life-threatening", "heavy bleeding").</summary>
        public List<string> impactCues = new List<string>();

        /// <summary>XML-configured cue lines from the matching Def (cues and/or description cue).</summary>
        public List<string> configuredCues = new List<string>();

        /// <summary>
        /// Maps one internal candidate to a public snapshot, copying every list so the snapshot is
        /// independent of the source after this call. Returns null when the candidate is null.
        /// </summary>
        internal static DiaryPromptEnchantmentCandidateSnapshot From(PromptEnchantmentCandidate candidate)
        {
            if (candidate == null)
            {
                return null;
            }

            return new DiaryPromptEnchantmentCandidateSnapshot
            {
                weight = candidate.weight,
                sourceHediffDefName = candidate.sourceHediffDefName ?? string.Empty,
                priorityText = candidate.priorityText ?? string.Empty,
                conditionText = candidate.conditionText ?? string.Empty,
                impactCues = CopyList(candidate.impactCues),
                configuredCues = CopyList(candidate.configuredCues)
            };
        }

        private static List<string> CopyList(List<string> source)
        {
            // Defensive copy. The internal lists are never null (field-initialized), but a future
            // author could clear one; this keeps the snapshot stable regardless.
            List<string> copy = new List<string>(source?.Count ?? 0);
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    string value = source[i];
                    if (value != null)
                    {
                        copy.Add(value);
                    }
                }
            }

            return copy;
        }
    }
}

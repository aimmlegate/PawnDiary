// Pure policy for composing the per-request voice block (psychotype + writing style + humor).
//
// The impure adapter (DiaryPipelineAdapters) resolves the localized wrapper frames and the pawn's
// rules on the main thread; this helper only owns the deterministic decision of WHICH voice layers
// a context-detail level renders. Keeping the decision here lets the pure test suite pin it without
// RimWorld (PROMPT_FOOTPRINT plan §8.3).
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"): pure code takes plain values and
// returns plain values — no Verse, no Translate, no settings reads.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Deterministic voice-layer selection per prompt context-detail level. Order is fixed by the
    /// adapter: psychotype → writing style → humor.
    /// </summary>
    internal static class VoiceBlockPolicy
    {
        /// <summary>
        /// Full and Balanced render all three voice layers; Compact omits the optional humor layer
        /// entirely so the two mandatory identity layers keep the model's attention. Invalid levels
        /// normalize to Full first, so hand-edited settings cannot silently drop humor.
        /// </summary>
        public static bool IncludeHumor(PromptContextDetailLevel level)
        {
            return PromptContextSelector.Normalize(level) != PromptContextDetailLevel.Compact;
        }
    }
}

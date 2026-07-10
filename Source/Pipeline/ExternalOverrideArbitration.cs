// Arbitration policy for the source-owned external overrides (writing style + psychotype): when TWO
// integration adapters both try to own the same pawn's override slot, which one wins?
//
// Rule (RimWorld convention — "the mod lower in the list wins conflicts"): when BOTH sourceIds can be
// resolved to an ACTIVE mod's load-order position, the LATER-loading mod may take the slot and the
// earlier-loading mod's write is rejected. When either side cannot be resolved (a sourceId is only a
// packageId by convention, and an uninstalled mod's saved owner id resolves to nothing), we keep the
// pre-arbitration behavior — last writer wins — so non-conforming adapters and stale owners behave
// exactly as before this policy existed (guardrail-only change, no ApiVersion bump).
//
// This file is PURE on purpose: it takes two already-resolved load-order indexes and answers yes/no,
// so the decision is unit-testable without RimWorld. The impure packageId -> load-order lookup lives
// at the edge in Source/Util/ExternalSourceLoadOrder.cs (SKILL.md architecture rule).
//
// New to C#/RimWorld? See AGENTS.md in the repo root.
namespace PawnDiary
{
    /// <summary>
    /// Pure decision: may a caller source displace another source's active external override?
    /// Indexes are mod load-order positions (0 = loads first); negative means "not resolvable to an
    /// active mod". Used by both the writing-style and psychotype override setters.
    /// </summary>
    public static class ExternalOverrideArbitration
    {
        /// <summary>Sentinel for "this sourceId is not the packageId of any active mod".</summary>
        public const int UnknownLoadOrder = -1;

        /// <summary>
        /// True when the caller may overwrite the owner's active override.
        /// Both indexes known: the later-loading (higher index) mod wins; an equal index means both
        /// ids resolved to the same mod, which is always allowed to update its own slot.
        /// Either index unknown: fall back to last-writer-wins (always allowed), preserving the
        /// pre-arbitration contract for free-form sourceIds and for owners whose mod was removed.
        /// </summary>
        /// <param name="callerLoadOrder">Load-order index of the writing source, or negative when unknown.</param>
        /// <param name="ownerLoadOrder">Load-order index of the current owner, or negative when unknown.</param>
        public static bool MayDisplace(int callerLoadOrder, int ownerLoadOrder)
        {
            if (callerLoadOrder < 0 || ownerLoadOrder < 0)
            {
                return true;
            }

            return callerLoadOrder >= ownerLoadOrder;
        }
    }
}

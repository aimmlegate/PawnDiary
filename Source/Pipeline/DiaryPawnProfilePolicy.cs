// Pure decisions for the pawn Diary profile's generation switch. The UI keeps edits in a draft,
// asks this policy what kind of apply is required, and leaves all RimWorld save mutation to the
// component adapter. This file intentionally has no Verse/Unity/settings dependencies.
using System;

namespace PawnDiary
{
    /// <summary>How an explicit profile Save should apply the pawn's generation-switch draft.</summary>
    internal enum DiaryPawnProfileGenerationDecision
    {
        /// <summary>The draft matches the saved switch; no generation mutation is needed.</summary>
        Unchanged,

        /// <summary>Save the switch as disabled. Existing pages and pending facts remain intact.</summary>
        Disable,

        /// <summary>Enable immediately because no resumable page backlog exists.</summary>
        EnableDirect,

        /// <summary>Ask before enabling because the existing setter will queue pending pages.</summary>
        EnableWithConfirmation
    }

    /// <summary>Pure policy for applying the profile's per-pawn generation switch.</summary>
    internal static class DiaryPawnProfilePolicy
    {
        /// <summary>
        /// Compares the saved and drafted switch. Only a disabled-to-enabled transition with at least
        /// one resumable page requires confirmation; corrupt negative counts behave as zero.
        /// </summary>
        public static DiaryPawnProfileGenerationDecision DecideGenerationChange(
            bool originalEnabled,
            bool draftEnabled,
            int resumableBacklogCount)
        {
            if (originalEnabled == draftEnabled)
            {
                return DiaryPawnProfileGenerationDecision.Unchanged;
            }

            if (!draftEnabled)
            {
                return DiaryPawnProfileGenerationDecision.Disable;
            }

            return Math.Max(0, resumableBacklogCount) > 0
                ? DiaryPawnProfileGenerationDecision.EnableWithConfirmation
                : DiaryPawnProfileGenerationDecision.EnableDirect;
        }
    }
}
